using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoRepoLineTracker.API.Features.CodeHealth;
using PoRepoLineTracker.API.Storage;

namespace PoRepoLineTracker.Unit.Features;

/// <summary>
/// The wiring between the two analysers, which is where the split can silently go wrong.
///
/// <para>C# is parsed by <c>RoslynMetricsAnalyzer</c> and everything else is estimated by
/// <c>CodeMetricsAnalyzer</c>. Both produce plausible numbers on their own, so a routing mistake —
/// C# sent to the heuristic, or counted twice — does not throw and does not look wrong. These
/// assert which analyser saw what.</para>
/// </summary>
public class GetCodeHealthQueryHandlerTests
{
    private static readonly RepositoryId RepoId = RepositoryId.New();
    private static readonly UserId OwnerId = UserId.New();

    private readonly IRepositoryDataService _repositories = Substitute.For<IRepositoryDataService>();
    private readonly IGitHubService _gitHub = Substitute.For<IGitHubService>();
    private readonly IUserPreferencesService _preferences = Substitute.For<IUserPreferencesService>();

    /// <summary>
    /// An empty memo on purpose: these tests are about which analyser sees which file, so every one
    /// must reach the compute path. A populated memo would short-circuit the handler and they would
    /// pass without exercising anything.
    /// </summary>
    private readonly ICodeHealthSnapshotStore _snapshots = Substitute.For<ICodeHealthSnapshotStore>();

    private GetCodeHealthQueryHandler CreateHandler(params SourceFile[] files)
    {
        _repositories.GetRepositoryByIdAsync(RepoId).Returns(new GitHubRepository
        {
            Id = RepoId,
            UserId = OwnerId,
            Owner = "acme",
            Name = "widgets"
        });

        _repositories.GetCommitLineCountsByRepositoryIdAsync(RepoId).Returns(new List<CommitLineCount>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RepositoryId = RepoId,
                CommitSha = "abcdef1234567890",
                CommitDate = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
                TotalLines = 100
            }
        });

        _preferences.GetFileExtensionsAsync(OwnerId).Returns(UserPreferences.DefaultFileExtensions);
        _gitHub.ResolveRepositoryPath(Arg.Any<string>()).Returns("/repo");
        _gitHub.EnumerateSourceFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(files);

        _snapshots.GetByRepositoryAsync(Arg.Any<RepositoryId>())
            .Returns(new Dictionary<string, CodeHealthSnapshotEntity>());

        return new GetCodeHealthQueryHandler(
            _repositories, _gitHub, _preferences, _snapshots, NullLogger<GetCodeHealthQueryHandler>.Instance);
    }

    private const string CSharpSource = """
        namespace Acme;

        public class Widget
        {
            public int Score(int a, int b)
            {
                if (a > b) return a;
                return b;
            }
        }
        """;

    [Fact]
    public async Task CSharpRepository_IsMeasuredByTheParser_NotTheHeuristic()
    {
        var handler = CreateHandler(new SourceFile("src/Widget.cs", ".cs", CSharpSource));

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report.Should().NotBeNull();
        report!.Metrics.Should().NotBeNull("the .cs file must reach the Roslyn analyser");
        report.Metrics!.MembersMeasured.Should().Be(1);
        report.Metrics.MaintainabilityIndex.Should().NotBeNull();
        report.Metrics.CyclomaticComplexity.Should().Be(2, "one baseline plus one if");

        // The heuristic never saw it, so it has no files to report on.
        report.FilesAnalyzed.Should().Be(0, "C# is parsed, not estimated — counting it twice would " +
            "put two maintainability numbers on one repository");
    }

    /// <summary>
    /// A C#-only repository gives the heuristic nothing, so its Build reports "no data". The report
    /// is not empty — it was measured by the other analyser — and saying otherwise would send the
    /// UI to its "nothing to measure" state for a fully-measured repository.
    /// </summary>
    [Fact]
    public async Task CSharpOnlyRepository_ReportsHasData_EvenThoughTheHeuristicSawNothing()
    {
        var handler = CreateHandler(new SourceFile("src/Widget.cs", ".cs", CSharpSource));

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report!.HasData.Should().BeTrue();
    }

    [Fact]
    public async Task NonCSharpRepository_KeepsTheHeuristic_AndReportsNoMetrics()
    {
        var handler = CreateHandler(new SourceFile("app/main.py", ".py",
            "def add(a, b):\n    if a > b:\n        return a\n    return b\n"));

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report!.Metrics.Should().BeNull("there is no C# to parse");
        report.FilesAnalyzed.Should().Be(1, "the other languages still get the line-oriented proxies");
        report.HasData.Should().BeTrue();
    }

    [Fact]
    public async Task MixedRepository_SplitsFilesBetweenTheTwoAnalysers()
    {
        var handler = CreateHandler(
            new SourceFile("src/Widget.cs", ".cs", CSharpSource),
            new SourceFile("app/main.py", ".py", "def add(a, b):\n    return a + b\n"),
            new SourceFile("web/app.ts", ".ts", "export const add = (a: number, b: number) => a + b;\n"));

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report!.Metrics!.FilesAnalyzed.Should().Be(1, "one .cs file");
        report.FilesAnalyzed.Should().Be(2, "the Python and TypeScript files");
    }

    /// <summary>
    /// Razor holds C#, but the text on disk is not a compilation unit — parsing it as C# yields a
    /// parse-error soup whose metrics would be noise wearing the label of a measurement.
    /// </summary>
    [Fact]
    public async Task RazorFiles_GoToTheHeuristic_NotTheCSharpParser()
    {
        var handler = CreateHandler(new SourceFile("Pages/Index.razor", ".razor",
            "@page \"/\"\n<h1>Hello</h1>\n@code { int X => 1; }\n"));

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report!.Metrics.Should().BeNull();
        report.FilesAnalyzed.Should().Be(1);
    }

    // ── The memo ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A commit already scored must never be re-walked. This is the whole point of the memo, and it
    /// is invisible without an explicit assertion: a broken cache still returns a correct report, it
    /// just quietly costs seconds of tree-walking and parsing on every single view.
    /// </summary>
    [Fact]
    public async Task CommitAlreadyInTheMemo_IsServedFromStorage_WithoutTouchingTheClone()
    {
        var handler = CreateHandler(new SourceFile("src/Widget.cs", ".cs", CSharpSource));

        // First pass computes and writes.
        var first = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);
        var stored = _snapshots.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ICodeHealthSnapshotStore.SaveAsync))
            .Select(c => (CodeHealthSnapshotEntity)c.GetArguments()[0]!)
            .Single();

        stored.RowKey.Should().Be("abcdef1234567890", "the memo is keyed on the commit, not the month");
        stored.ReportJson.Should().NotBeEmpty();

        // Second pass: the memo now holds that commit.
        _gitHub.ClearReceivedCalls();
        _snapshots.GetByRepositoryAsync(Arg.Any<RepositoryId>())
            .Returns(new Dictionary<string, CodeHealthSnapshotEntity> { [stored.RowKey] = stored });

        var second = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        second!.OverallScore.Should().Be(first!.OverallScore);
        _gitHub.DidNotReceive().EnumerateSourceFiles(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }

    /// <summary>
    /// A memo row whose document cannot be read — written before a shape change, or truncated — must
    /// fall through to a recompute rather than surfacing a null report or throwing.
    /// </summary>
    [Fact]
    public async Task UnreadableMemoRow_FallsBackToRecomputing()
    {
        var handler = CreateHandler(new SourceFile("src/Widget.cs", ".cs", CSharpSource));

        _snapshots.GetByRepositoryAsync(Arg.Any<RepositoryId>())
            .Returns(new Dictionary<string, CodeHealthSnapshotEntity>
            {
                ["abcdef1234567890"] = new() { RowKey = "abcdef1234567890", ReportJson = "{ not json" }
            });

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report.Should().NotBeNull();
        report!.Metrics!.MembersMeasured.Should().Be(1, "the bad row must degrade to a recompute");
    }

    [Fact]
    public async Task RepositoryWithNoAnalysedCommits_ReturnsNull()
    {
        _repositories.GetRepositoryByIdAsync(RepoId).Returns(new GitHubRepository
        {
            Id = RepoId, UserId = OwnerId, Owner = "acme", Name = "widgets"
        });
        _repositories.GetCommitLineCountsByRepositoryIdAsync(RepoId).Returns([]);

        _snapshots.GetByRepositoryAsync(Arg.Any<RepositoryId>())
            .Returns(new Dictionary<string, CodeHealthSnapshotEntity>());

        var handler = new GetCodeHealthQueryHandler(
            _repositories, _gitHub, _preferences, _snapshots, NullLogger<GetCodeHealthQueryHandler>.Instance);

        var report = await handler.Handle(new GetCodeHealthQuery(RepoId), CancellationToken.None);

        report.Should().BeNull("a report of zeroes reads as genuinely terrible code");
    }
}
