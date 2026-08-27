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
    public async Task CountsOnlyTheConfiguredExtensions()
    {
        var sha = Commit(
            ("src/a.cs", "var x = 1;\nvar y = 2;\n"),
            ("src/notes.txt", "not counted\nnot counted\n"));

        var counts = await CountAsync(sha);

        counts.Should().ContainKey(".cs").WhoseValue.Should().Be(2);
        counts.Should().NotContainKey(".txt");
    }

    /// <summary>
    /// The point of the memo. Two commits sharing an untouched directory must report the same
    /// numbers for it — a memo keyed or invalidated wrongly shows up exactly here.
    /// </summary>
    [Fact]
    public async Task UnchangedDirectory_ReportsTheSameCountsAcrossCommits()
    {
        var first = Commit(
            ("stable/a.cs", "var a = 1;\nvar b = 2;\nvar c = 3;\n"),
            ("moving/b.cs", "var d = 4;\n"));

        // Only `moving/` changes; `stable/` keeps its tree id and must be served from the memo.
        var second = Commit(("moving/b.cs", "var d = 4;\nvar e = 5;\n"));

        var firstCounts = await CountAsync(first);
        var secondCounts = await CountAsync(second);

        firstCounts[".cs"].Should().Be(4);
        secondCounts[".cs"].Should().Be(5, "only the changed file adds a line");
    }

    /// <summary>
    /// A commit whose root tree is unchanged is answered entirely from the memo. It must still
    /// return the real numbers rather than an empty result.
    /// </summary>
    [Fact]
    public async Task EmptyCommit_WithAnIdenticalTree_ReportsTheSameCounts()
    {
        var first = Commit(("src/a.cs", "var x = 1;\nvar y = 2;\n"));
        var second = Commit(); // no file changes — same root tree

        var firstCounts = await CountAsync(first);
        var secondCounts = await CountAsync(second);

        secondCounts.Should().BeEquivalentTo(firstCounts);
        secondCounts[".cs"].Should().Be(2);
    }

    /// <summary>
    /// The memo hands back its own dictionaries internally. If one of those reaches a caller, the
    /// caller mutating its result would corrupt every later commit that shares the tree — so the
    /// public method must return a copy.
    /// </summary>
    [Fact]
    public async Task ReturnedDictionary_IsNotTheMemoisedInstance()
    {
        var sha = Commit(("src/a.cs", "var x = 1;\n"));

        var first = await CountAsync(sha);
        first[".cs"] = 9_999;

        var second = await CountAsync(sha);

        second[".cs"].Should().Be(1, "a caller mutating its own result must not poison the memo");
    }

    /// <summary>
    /// Counted extensions come from user preferences verbatim while the lookup key is
    /// lower-cased, so the comparison has to ignore case — a preference saved as ".CS" used to
    /// match nothing and silently count zero.
    /// </summary>
    [Fact]
    public async Task ExtensionMatching_IgnoresCase()
    {
        var sha = Commit(("src/a.cs", "var x = 1;\n"));

        var counts = await _sut.CountLinesInCommitAsync(_repoPath, sha, [".CS"]);

        counts.Should().ContainKey(".cs").WhoseValue.Should().Be(1);
    }

    /// <summary>
    /// Changing which extensions are counted must invalidate the memo. Returning the previous
    /// answer would make a settings change followed by a re-analysis appear to do nothing.
    /// </summary>
    [Fact]
    public async Task ChangingTheCountedExtensions_ChangesTheResult()
    {
        var sha = Commit(("src/a.cs", "var x = 1;\n"), ("src/a.css", "body { color: red; }\n"));

        var both = await _sut.CountLinesInCommitAsync(_repoPath, sha, [".cs", ".css"]);
        var onlyCs = await _sut.CountLinesInCommitAsync(_repoPath, sha, [".cs"]);

        both.Should().ContainKey(".css");
        onlyCs.Should().NotContainKey(".css", "the memo must not answer for a different extension set");
        onlyCs[".cs"].Should().Be(1);
    }

    /// <summary>
    /// The measurable point of the memo, asserted as behaviour rather than as a timing.
    ///
    /// <para>Replaying history used to decompress and count every blob of every commit, so
    /// analysis cost scaled with commits × repository size instead of with the amount of code that
    /// actually changed. Here three commits touch one file in <c>moving/</c> while <c>stable/</c>
    /// never changes — so the stable blobs must be read exactly ONCE across the whole replay.</para>
    /// </summary>
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

        foreach (var sha in shas)
        {
            await sut.CountLinesInCommitAsync(_repoPath, sha, [".cs"]);
        }

        counter.ReadsOf("var a = 1;\n").Should().Be(1,
            "stable/a.cs never changed, so its content must be counted once across all three commits");
        counter.ReadsOf("var b = 1;\n").Should().Be(1);

        // The file that actually changed is genuinely different content each time, so it is read
        // once per version — which is the work the analysis is actually for.
        counter.TotalReads.Should().Be(5, "2 stable files read once each, plus 3 distinct versions of moving/c.cs");
    }

    /// <summary>Records every blob it is asked to count, so a test can assert on re-reads.</summary>
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

    [Fact]
    public async Task UnknownCommit_ReturnsEmptyRatherThanThrowing()
    {
        Commit(("src/a.cs", "var x = 1;\n"));

        var counts = await CountAsync(new string('a', 40));

        counts.Should().BeEmpty();
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
