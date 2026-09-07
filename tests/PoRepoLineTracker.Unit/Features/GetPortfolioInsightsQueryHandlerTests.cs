using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Features.Insights;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The Insights dashboard is one query over every commit of every repository, and every figure it
/// prints is arithmetic with a way to be subtly wrong: a snapshot summed instead of taken, a
/// baseline read from the wrong end of a window, a streak that counts today as broken before the
/// day is over. None of it had a test.
///
/// <para>The handler is exercised through <c>Handle</c> rather than its private helpers, so these
/// assert the figures the page actually renders — one test per behaviour, with the assertions for
/// a behaviour grouped over a single arranged portfolio.</para>
/// </summary>
public class GetPortfolioInsightsQueryHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly GetPortfolioInsightsQueryHandler _sut;
    private readonly UserId _userId = UserId.New();

    /// <summary>Anchored to UTC midnight because the handler's windows are all day-aligned.</summary>
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    public GetPortfolioInsightsQueryHandlerTests()
    {
        _sut = new GetPortfolioInsightsQueryHandler(
            _dataService, Substitute.For<ILogger<GetPortfolioInsightsQueryHandler>>());
    }

    private GitHubRepository GivenRepository(string owner, string name, params CommitLineCount[] commits)
    {
        var repo = new GitHubRepository
        {
            Id = RepositoryId.New(),
            UserId = _userId,
            Owner = owner,
            Name = name,
            LastAnalyzedCommitDate = commits.Length > 0 ? commits.Max(c => c.CommitDate) : null
        };

        foreach (var commit in commits) commit.RepositoryId = repo.Id;
        _dataService.GetCommitLineCountsByRepositoryIdAsync(repo.Id).Returns(commits.ToList());
        return repo;
    }

    /// <summary>Captured per-repo commits; lifted into the new GetAllRepositoriesWithCommitsAsync stub.</summary>
    private readonly List<GitHubRepository> _reposForLiftedStub = new();

    private void GivenRepositories(params GitHubRepository[] repositories)
    {
        _dataService.GetAllRepositoriesAsync(_userId).Returns(repositories.ToList());

        // The handler now reads commits through the lifted fan-out, which pairs each repository
        // with its commits already ordered by date ascending. Build that map from the per-repo
        // setups above and hand it to the substitute.
        _reposForLiftedStub.Clear();
        _reposForLiftedStub.AddRange(repositories);

        var pairs = repositories
            .Select(repo => (
                Repository: repo,
                Commits: (IReadOnlyList<CommitLineCount>)_dataService
                    .GetCommitLineCountsByRepositoryIdAsync(repo.Id)
                    .GetAwaiter()
                    .GetResult()
                    .OrderBy(c => c.CommitDate)
                    .ToList()))
            .ToList();

        _dataService.GetAllRepositoriesWithCommitsAsync(_userId).Returns(pairs);
    }

    private static CommitLineCount Commit(
        int daysAgo,
        int totalLines,
        int linesAdded = 0,
        int linesRemoved = 0,
        Dictionary<string, int>? byFileType = null) => new()
        {
            Id = Guid.NewGuid(),
            CommitSha = Guid.NewGuid().ToString("N")[..7],
            CommitDate = Today.AddDays(-daysAgo),
            TotalLines = totalLines,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            LinesByFileType = byFileType ?? []
        };

    private Task<PortfolioInsightsDto> WhenQueried() =>
        _sut.Handle(new GetPortfolioInsightsQuery(_userId), CancellationToken.None);

    // ─── Totals ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Totals_HandlesEmptyRepositoriesAndSumsNewestSnapshots()
    {
        GivenRepositories();
        var empty = await WhenQueried();
        empty.RepositoryCount.Should().Be(0);
        empty.TotalLines.Should().Be(0);
        empty.Movers.Should().BeEmpty();
        empty.LanguageMix.Should().BeEmpty();
        empty.Activity.Should().BeEmpty();

        GivenRepositories(
            GivenRepository("acme", "api",
                Commit(daysAgo: 10, totalLines: 400),
                Commit(daysAgo: 5, totalLines: 700),
                Commit(daysAgo: 1, totalLines: 1_000)),
            GivenRepository("acme", "archived", Commit(daysAgo: 800, totalLines: 5_000)),
            GivenRepository("acme", "never-analyzed"));

        var insights = await WhenQueried();
        insights.TotalLines.Should().Be(6_000);
        insights.RepositoryCount.Should().Be(3);
        insights.AnalyzedCount.Should().Be(2);
    }

    // ─── Net change ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NetLines_MeasuresAgainstSnapshotsAndHandlesNewRepositories()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 60, totalLines: 1_000),   // before both windows
            Commit(daysAgo: 20, totalLines: 1_400),   // inside 30d, before 7d
            Commit(daysAgo: 2, totalLines: 1_500)));

        var insights = await WhenQueried();
        insights.NetLines30Days.Should().Be(500);
        insights.NetLines7Days.Should().Be(100);

        GivenRepositories(GivenRepository("acme", "brand-new", Commit(daysAgo: 3, totalLines: 900)));
        var brandNew = await WhenQueried();
        brandNew.NetLines30Days.Should().Be(900);
    }

    // ─── Movers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Movers_RankedByNetChangeGainersFirst()
    {
        GivenRepositories(
            GivenRepository("acme", "shrinking",
                Commit(daysAgo: 40, totalLines: 1_000),
                Commit(daysAgo: 1, totalLines: 400)),
            GivenRepository("acme", "growing",
                Commit(daysAgo: 40, totalLines: 100),
                Commit(daysAgo: 1, totalLines: 900)),
            GivenRepository("acme", "empty"));

        var insights = await WhenQueried();
        insights.Movers.Should().HaveCount(2);
        insights.Movers[0].Name.Should().Be("growing");
        insights.Movers[0].NetChange30Days.Should().Be(800);
        insights.Movers[^1].Name.Should().Be("shrinking");
        insights.Movers[^1].NetChange30Days.Should().Be(-600);
    }

    // ─── Language mix ────────────────────────────────────────────────────────

    [Fact]
    public async Task LanguageMix_RanksNewestSnapshotsAndFoldsBeyondTopEight()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 10, totalLines: 500, byFileType: new Dictionary<string, int> { [".js"] = 500 }),
            Commit(daysAgo: 1, totalLines: 1_000, byFileType: new Dictionary<string, int>
            {
                [".cs"] = 750,
                [".razor"] = 250
            })));

        var insights = await WhenQueried();
        insights.LanguageMix.Select(l => l.Extension).Should().ContainInOrder(".cs", ".razor");
        insights.LanguageMix.Should().NotContain(l => l.Extension == ".js");
        insights.LanguageMix.Sum(l => l.Percentage).Should().BeApproximately(100, 0.1);
        insights.LanguageMix[0].Lines.Should().Be(750);

        var byFileType = Enumerable.Range(1, 12)
            .ToDictionary(i => $".ext{i:00}", i => 100 - i);
        GivenRepositories(GivenRepository("acme", "api12",
            Commit(daysAgo: 1, totalLines: byFileType.Values.Sum(), byFileType: byFileType)));

        var topEight = await WhenQueried();
        topEight.LanguageMix.Should().HaveCount(9);
        topEight.LanguageMix[^1].Extension.Should().Be("Other");
        topEight.LanguageMix.Sum(l => l.Percentage).Should().BeApproximately(100, 0.2);
    }

    // ─── Streaks and the heatmap ─────────────────────────────────────────────

    [Fact]
    public async Task StreaksAndActivity_CalculatesRunsAndIncludesZeroCommitDays()
    {
        GivenRepositories(GivenRepository("acme", "missed",
            Commit(daysAgo: 5, totalLines: 100),
            Commit(daysAgo: 4, totalLines: 200)));

        var zeroStreak = await WhenQueried();
        zeroStreak.CurrentStreakDays.Should().Be(0);

        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 203, totalLines: 10),
            Commit(daysAgo: 202, totalLines: 20),
            Commit(daysAgo: 201, totalLines: 30),
            Commit(daysAgo: 200, totalLines: 40),
            Commit(daysAgo: 2, totalLines: 50),
            Commit(daysAgo: 1, totalLines: 60)));

        var streaks = await WhenQueried();
        streaks.LongestStreakDays.Should().Be(4);
        streaks.CurrentStreakDays.Should().Be(2);

        GivenRepositories(GivenRepository("acme", "api2",
            Commit(daysAgo: 400, totalLines: 50, linesAdded: 50),
            Commit(daysAgo: 45, totalLines: 100, linesAdded: 50),
            Commit(daysAgo: 3, totalLines: 150, linesAdded: 25),
            Commit(daysAgo: 3, totalLines: 175, linesAdded: 15),
            Commit(daysAgo: 1, totalLines: 235, linesAdded: 60)));

        var activity = await WhenQueried();
        activity.Activity.Should().HaveCount(365);
        activity.Activity.Select(a => a.Date).Should().BeInAscendingOrder();
        activity.Activity.Should().OnlyHaveUniqueItems(a => a.Date);
        activity.Activity.Count(a => a.Commits > 0).Should().Be(3);
        activity.Activity.Single(a => a.Date == Today.AddDays(-1)).LinesAdded.Should().Be(60);
        activity.Commits30Days.Should().Be(3);
        activity.ActiveDays30.Should().Be(2);
        activity.LinesAdded30Days.Should().Be(100);
    }
}
