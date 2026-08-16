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

    private void GivenRepositories(params GitHubRepository[] repositories) =>
        _dataService.GetAllRepositoriesAsync(_userId).Returns(repositories.ToList());

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
    public async Task NoRepositories_ReturnsAnEmptyShapeRatherThanNulls()
    {
        GivenRepositories();

        var insights = await WhenQueried();

        insights.RepositoryCount.Should().Be(0);
        insights.TotalLines.Should().Be(0);
        insights.Movers.Should().BeEmpty();
        insights.LanguageMix.Should().BeEmpty();
        insights.Activity.Should().BeEmpty();
    }

    /// <summary>
    /// The single most dangerous mistake in this handler: TotalLines on a commit is a snapshot of
    /// the whole repository, so summing it across commits counts the same lines once per commit.
    /// Three commits of a 1,000-line repo is 1,000 lines, not 3,000. Across repositories the
    /// snapshots DO sum, a repository last committed to outside every chart window still has its
    /// code, and only repositories with an analysis date count as analysed.
    /// </summary>
    [Fact]
    public async Task TotalLines_SumsTheNewestSnapshotPerRepository_HoweverOldItIs()
    {
        GivenRepositories(
            GivenRepository("acme", "api",
                Commit(daysAgo: 10, totalLines: 400),
                Commit(daysAgo: 5, totalLines: 700),
                Commit(daysAgo: 1, totalLines: 1_000)),
            GivenRepository("acme", "archived", Commit(daysAgo: 800, totalLines: 5_000)),
            GivenRepository("acme", "never-analyzed"));

        var insights = await WhenQueried();

        insights.TotalLines.Should().Be(6_000, "the newest snapshot per repository, summed across repositories");
        insights.RepositoryCount.Should().Be(3);
        insights.AnalyzedCount.Should().Be(2);
    }

    // ─── Net change ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NetLines_IsMeasuredAgainstTheSnapshotAtTheWindowEdge()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 60, totalLines: 1_000),   // before both windows
            Commit(daysAgo: 20, totalLines: 1_400),   // inside 30d, before 7d
            Commit(daysAgo: 2, totalLines: 1_500)));

        var insights = await WhenQueried();

        insights.NetLines30Days.Should().Be(500, "the 30-day baseline is the 1,000-line snapshot");
        insights.NetLines7Days.Should().Be(100, "the 7-day baseline is the 1,400-line snapshot");
    }

    /// <summary>
    /// A repository that did not exist 30 days ago has no snapshot that old, so its baseline is 0
    /// and its whole size reads as growth — which is the truthful answer to "how much did this add
    /// in 30 days". (The negative direction — baseline above the current snapshot — is asserted by
    /// the movers test below via the shrinking repository.)
    /// </summary>
    [Fact]
    public async Task NetLines_TreatsAWhollyNewRepositoryAsAllGrowth()
    {
        GivenRepositories(GivenRepository("acme", "brand-new", Commit(daysAgo: 3, totalLines: 900)));

        var insights = await WhenQueried();

        insights.NetLines30Days.Should().Be(900);
    }

    // ─── Movers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Movers_AreRankedByNetChange_GainersFirst_OmittingCommitlessRepositories()
    {
        // Both carry a pre-window commit so the 30-day baseline is a real snapshot rather than 0.
        GivenRepositories(
            GivenRepository("acme", "shrinking",
                Commit(daysAgo: 40, totalLines: 1_000),
                Commit(daysAgo: 1, totalLines: 400)),
            GivenRepository("acme", "growing",
                Commit(daysAgo: 40, totalLines: 100),
                Commit(daysAgo: 1, totalLines: 900)),
            GivenRepository("acme", "empty"));

        var insights = await WhenQueried();

        insights.Movers.Should().HaveCount(2, "a repository with no commits at all has nothing to rank");
        insights.Movers[0].Name.Should().Be("growing");
        insights.Movers[0].NetChange30Days.Should().Be(800);
        insights.Movers[^1].Name.Should().Be("shrinking");
        insights.Movers[^1].NetChange30Days.Should().Be(-600);
    }

    // ─── Language mix ────────────────────────────────────────────────────────

    /// <summary>Only the current snapshot's breakdown counts — the mix is "what is there now".</summary>
    [Fact]
    public async Task LanguageMix_RanksTheNewestSnapshotOnly_AndSumsToOneHundredPercent()
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
        insights.LanguageMix.Should().NotContain(l => l.Extension == ".js",
            "the superseded snapshot's breakdown is not what is there now");
        insights.LanguageMix.Sum(l => l.Percentage).Should().BeApproximately(100, 0.1);
        insights.LanguageMix[0].Lines.Should().Be(750);
    }

    [Fact]
    public async Task LanguageMix_FoldsEverythingPastTheTopEightIntoOther()
    {
        var byFileType = Enumerable.Range(1, 12)
            .ToDictionary(i => $".ext{i:00}", i => 100 - i); // strictly descending, so ranking is unambiguous

        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 1, totalLines: byFileType.Values.Sum(), byFileType: byFileType)));

        var insights = await WhenQueried();

        insights.LanguageMix.Should().HaveCount(9, "eight named extensions plus Other");
        insights.LanguageMix[^1].Extension.Should().Be("Other");
        insights.LanguageMix.Sum(l => l.Percentage).Should().BeApproximately(100, 0.2);
    }

    // ─── Streaks and the heatmap ─────────────────────────────────────────────

    [Fact]
    public async Task CurrentStreak_IsZeroOnceTwoDaysHaveBeenMissed()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 5, totalLines: 100),
            Commit(daysAgo: 4, totalLines: 200)));

        var insights = await WhenQueried();

        insights.CurrentStreakDays.Should().Be(0);
    }

    /// <summary>
    /// A day with no commits *yet* must not read as having broken the streak — it is not over.
    /// This is the case an off-by-one in CurrentStreak silently gets wrong every morning: the
    /// recent run here ends yesterday and must still count as the current streak.
    /// </summary>
    [Fact]
    public async Task LongestStreak_FindsTheLongestRunAnywhereInTheYear()
    {
        GivenRepositories(GivenRepository("acme", "api",
            // A four-day run months back...
            Commit(daysAgo: 203, totalLines: 10),
            Commit(daysAgo: 202, totalLines: 20),
            Commit(daysAgo: 201, totalLines: 30),
            Commit(daysAgo: 200, totalLines: 40),
            // ...and a shorter, more recent one.
            Commit(daysAgo: 2, totalLines: 50),
            Commit(daysAgo: 1, totalLines: 60)));

        var insights = await WhenQueried();

        insights.LongestStreakDays.Should().Be(4);
        insights.CurrentStreakDays.Should().Be(2, "a today with no commits yet has not broken the streak");
    }

    /// <summary>
    /// The heatmap draws a fixed grid, so a sparse list would silently shift every cell after the
    /// first gap — a quiet week would redraw the whole year wrong. The same arrangement pins the
    /// window rules: commits outside 30 days do not count towards the 30-day figures, commits
    /// outside the year do not appear in the grid at all, and active days are distinct days, not
    /// commits.
    /// </summary>
    [Fact]
    public async Task Activity_IncludesZeroCommitDays_AndAppliesTheWindowBoundaries()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 400, totalLines: 50, linesAdded: 50),   // outside the year window
            Commit(daysAgo: 45, totalLines: 100, linesAdded: 50),   // inside the year, outside 30d
            Commit(daysAgo: 3, totalLines: 150, linesAdded: 25),
            Commit(daysAgo: 3, totalLines: 175, linesAdded: 15),
            Commit(daysAgo: 1, totalLines: 235, linesAdded: 60)));

        var insights = await WhenQueried();

        insights.Activity.Should().HaveCount(365);
        insights.Activity.Select(a => a.Date).Should().BeInAscendingOrder();
        insights.Activity.Should().OnlyHaveUniqueItems(a => a.Date);
        insights.Activity.Count(a => a.Commits > 0).Should().Be(3, "the day-400 commit is outside the grid");
        insights.Activity.Single(a => a.Date == Today.AddDays(-1)).LinesAdded.Should().Be(60);

        insights.Commits30Days.Should().Be(3);
        insights.ActiveDays30.Should().Be(2, "distinct days, not commits");
        insights.LinesAdded30Days.Should().Be(100);
    }
}
