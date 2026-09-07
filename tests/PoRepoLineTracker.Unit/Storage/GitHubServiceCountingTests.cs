using FluentAssertions;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Services;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Line counting driven against a REAL git repository built on disk, because the behaviour under
/// test is about git object identity and nothing substituted can exercise that.
///
/// <para>The counter memoises results on tree and blob object ids so that replaying a repository's
/// history does not re-walk unchanged directories — a 4,000-commit repository previously
/// decompressed every blob of every commit. A memo that returns a stale or shared dictionary is
/// the obvious way for that to go wrong, and it would show up as line counts that drift between
/// commits rather than as a crash. These pin it.</para>
/// </summary>
public sealed class GitHubServiceCountingTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitHubService _sut;

    private static readonly List<string> CountedExtensions = [".cs", ".css"];

    public GitHubServiceCountingTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "porepo-count-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoPath);
        Repository.Init(_repoPath);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        _sut = new GitHubService(
            new HttpClient(),
            configuration,
            Substitute.For<ILogger<GitHubService>>(),
            SourceLineCounter.DefaultSet(),
            new GitClient(configuration, Substitute.For<ILogger<GitClient>>()),
            new FileIgnoreFilter(Substitute.For<ILogger<FileIgnoreFilter>>()));
    }

    /// <summary>Writes the given files (path → content) and commits them, returning the commit SHA.</summary>
    private string Commit(params (string Path, string Content)[] files)
    {
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(_repoPath, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        using var repo = new Repository(_repoPath);
        Commands.Stage(repo, "*");
        var author = new Signature("t", "t@example.com", DateTimeOffset.UtcNow);
        return repo.Commit("c", author, author, new CommitOptions { AllowEmptyCommit = true }).Sha;
    }

    private Task<Dictionary<string, int>> CountAsync(string sha) =>
        _sut.CountLinesInCommitAsync(_repoPath, sha, CountedExtensions);

    [Fact]
    public async Task CountsConfiguredExtensions_IgnoringCaseAndHandlingUnknownCommits()
    {
        var sha = Commit(
            ("src/a.cs", "var x = 1;\nvar y = 2;\n"),
            ("src/notes.txt", "not counted\nnot counted\n"));

        var counts = await CountAsync(sha);
        counts.Should().ContainKey(".cs").WhoseValue.Should().Be(2);
        counts.Should().NotContainKey(".txt");

        var caseInsensitiveCounts = await _sut.CountLinesInCommitAsync(_repoPath, sha, [".CS"]);
        caseInsensitiveCounts.Should().ContainKey(".cs").WhoseValue.Should().Be(2);

        var unknownCounts = await CountAsync(new string('a', 40));
        unknownCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task UnchangedDirectoryAndEmptyCommit_ServedFromMemo()
    {
        var first = Commit(
            ("stable/a.cs", "var a = 1;\nvar b = 2;\nvar c = 3;\n"),
            ("moving/b.cs", "var d = 4;\n"));

        var second = Commit(("moving/b.cs", "var d = 4;\nvar e = 5;\n"));

        var firstCounts = await CountAsync(first);
        var secondCounts = await CountAsync(second);

        firstCounts[".cs"].Should().Be(4);
        secondCounts[".cs"].Should().Be(5);

        var emptyCommit = Commit();
        var emptyCounts = await CountAsync(emptyCommit);
        emptyCounts.Should().BeEquivalentTo(secondCounts);
    }

    [Fact]
    public async Task MemoIsolationAndInvalidation_GuardsInstanceAndReevaluatesOnChange()
    {
        var sha = Commit(("src/a.cs", "var x = 1;\n"), ("src/a.css", "body { color: red; }\n"));

        var first = await CountAsync(sha);
        first[".cs"] = 9_999;

        var second = await CountAsync(sha);
        second[".cs"].Should().Be(1);

        var both = await _sut.CountLinesInCommitAsync(_repoPath, sha, [".cs", ".css"]);
        var onlyCs = await _sut.CountLinesInCommitAsync(_repoPath, sha, [".cs"]);

        both.Should().ContainKey(".css");
        onlyCs.Should().NotContainKey(".css");
        onlyCs[".cs"].Should().Be(1);
    }

    [Fact]
    public async Task ReplayingHistory_ReadsAnUnchangedFileOnlyOnce()
    {
        var counter = new CountingLineCounter(".cs");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var sut = new GitHubService(
            new HttpClient(),
            configuration,
            Substitute.For<ILogger<GitHubService>>(),
            [counter, new SourceLineCounter("*")],
            new GitClient(configuration, Substitute.For<ILogger<GitClient>>()),
            new FileIgnoreFilter(Substitute.For<ILogger<FileIgnoreFilter>>()));

        var shas = new List<string>
        {
            Commit(("stable/a.cs", "var a = 1;\n"), ("stable/b.cs", "var b = 1;\n"), ("moving/c.cs", "var c = 1;\n")),
            Commit(("moving/c.cs", "var c = 2;\n")),
            Commit(("moving/c.cs", "var c = 3;\n"))
        };

        foreach (var s in shas)
        {
            await sut.CountLinesInCommitAsync(_repoPath, s, [".cs"]);
        }

        counter.ReadsOf("var a = 1;\n").Should().Be(1);
        counter.ReadsOf("var b = 1;\n").Should().Be(1);
        counter.TotalReads.Should().Be(5);
    }

    private sealed class CountingLineCounter(string extension) : ILineCounter
    {
        private readonly List<string> _read = [];

        public string FileExtension { get; } = extension;

        public int TotalReads => _read.Count;

        public int ReadsOf(string content) => _read.Count(c => c == content);

        public async Task<int> CountLinesAsync(Stream stream)
        {
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            _read.Add(content);
            return content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    public void Dispose()
    {
        try
        {
            // Git marks object files read-only, which blocks a plain recursive delete on Windows.
            foreach (var file in Directory.EnumerateFiles(_repoPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_repoPath, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a test run over.
        }
    }
}
