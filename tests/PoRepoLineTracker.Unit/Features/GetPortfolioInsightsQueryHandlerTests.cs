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
/// assert the figures the page actually renders.</para>
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
        double aiPercentage = 0,
        Dictionary<string, int>? byFileType = null) => new()
        {
            Id = Guid.NewGuid(),
            CommitSha = Guid.NewGuid().ToString("N")[..7],
            CommitDate = Today.AddDays(-daysAgo),
            TotalLines = totalLines,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            AiPercentage = aiPercentage,
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
    /// Three commits of a 1,000-line repo is 1,000 lines, not 3,000.
    /// </summary>
    [Fact]
    public async Task TotalLines_TakesTheNewestSnapshot_NeverTheSumOfThem()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 10, totalLines: 400),
            Commit(daysAgo: 5, totalLines: 700),
            Commit(daysAgo: 1, totalLines: 1_000)));

        var insights = await WhenQueried();

        insights.TotalLines.Should().Be(1_000);
    }

    [Fact]
    public async Task TotalLines_SumsAcrossRepositories()
    {
        GivenRepositories(
            GivenRepository("acme", "api", Commit(daysAgo: 1, totalLines: 1_000)),
            GivenRepository("acme", "web", Commit(daysAgo: 1, totalLines: 250)));

        var insights = await WhenQueried();

        insights.TotalLines.Should().Be(1_250);
    }

    /// <summary>
    /// The discrepancy RepositoryTotals exists to close. A repository last committed to well
    /// outside any chart window still has its code, and must still count.
    /// </summary>
    [Fact]
    public async Task TotalLines_CountsARepositoryWhoseLastCommitPredatesEveryWindow()
    {
        GivenRepositories(GivenRepository("acme", "archived", Commit(daysAgo: 800, totalLines: 5_000)));

        var insights = await WhenQueried();

        insights.TotalLines.Should().Be(5_000);
    }

    [Fact]
    public async Task AnalyzedCount_CountsOnlyRepositoriesWithAnAnalysisDate()
    {
        GivenRepositories(
            GivenRepository("acme", "api", Commit(daysAgo: 1, totalLines: 100)),
            GivenRepository("acme", "never-analyzed"));

        var insights = await WhenQueried();

        insights.RepositoryCount.Should().Be(2);
        insights.AnalyzedCount.Should().Be(1);
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
    /// Note the day-40 commit: it is what establishes the baseline. A repository whose entire
    /// history sits inside the window has no snapshot at its edge, so its baseline is 0 and it
    /// reads as all growth — see the test below.
    /// </summary>
    [Fact]
    public async Task NetLines_IsNegativeWhenCodeWasDeleted()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 40, totalLines: 2_000),
            Commit(daysAgo: 1, totalLines: 1_200)));

        var insights = await WhenQueried();

        insights.NetLines30Days.Should().Be(-800);
    }

    /// <summary>
    /// A repository that did not exist 30 days ago has no snapshot that old, so its baseline is 0
    /// and its whole size reads as growth — which is the truthful answer to "how much did this add
    /// in 30 days".
    /// </summary>
    [Fact]
    public async Task NetLines_TreatsAWhollyNewRepositoryAsAllGrowth()
    {
        GivenRepositories(GivenRepository("acme", "brand-new", Commit(daysAgo: 3, totalLines: 900)));

        var insights = await WhenQueried();

        insights.NetLines30Days.Should().Be(900);
    }

    // ─── Recent activity ─────────────────────────────────────────────────────

    [Fact]
    public async Task Commits30Days_ExcludesCommitsOlderThanTheWindow()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 45, totalLines: 100, linesAdded: 100),
            Commit(daysAgo: 20, totalLines: 200, linesAdded: 100),
            Commit(daysAgo: 5, totalLines: 300, linesAdded: 100)));

        var insights = await WhenQueried();

        insights.Commits30Days.Should().Be(2);
        insights.LinesAdded30Days.Should().Be(200);
    }

    /// <summary>
    /// Weighted by lines added, not averaged across commits. A one-line human commit and a
    /// 2,000-line generated file are not the same event, and a plain mean would rank them equally.
    /// </summary>
    [Fact]
    public async Task AiPercentage_IsWeightedByLinesAdded_NotAveragedPerCommit()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 5, totalLines: 2_001, linesAdded: 2_000, aiPercentage: 100),
            Commit(daysAgo: 4, totalLines: 2_002, linesAdded: 1, aiPercentage: 0)));

        var insights = await WhenQueried();

        // A per-commit mean would say 50. Weighted, it is 2000/2001 ≈ 100.
        insights.AiPercentage30Days.Should().BeApproximately(100, 0.1);
    }

    [Fact]
    public async Task AiPercentage_IsZeroWhenNothingWasAddedInTheWindow()
    {
        GivenRepositories(GivenRepository("acme", "api", Commit(daysAgo: 90, totalLines: 100, linesAdded: 100, aiPercentage: 90)));

        var insights = await WhenQueried();

        insights.AiPercentage30Days.Should().Be(0);
    }

    // ─── Movers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Movers_AreRankedByNetChange_GainersFirst()
    {
        // Both carry a pre-window commit so the 30-day baseline is a real snapshot rather than 0.
        GivenRepositories(
            GivenRepository("acme", "shrinking",
                Commit(daysAgo: 40, totalLines: 1_000),
                Commit(daysAgo: 1, totalLines: 400)),
            GivenRepository("acme", "growing",
                Commit(daysAgo: 40, totalLines: 100),
                Commit(daysAgo: 1, totalLines: 900)));

        var insights = await WhenQueried();

        insights.Movers.Should().HaveCount(2);
        insights.Movers[0].Name.Should().Be("growing");
        insights.Movers[0].NetChange30Days.Should().Be(800);
        insights.Movers[^1].Name.Should().Be("shrinking");
        insights.Movers[^1].NetChange30Days.Should().Be(-600);
    }

    [Fact]
    public async Task Movers_OmitARepositoryWithNoCommitsAtAll()
    {
        GivenRepositories(
            GivenRepository("acme", "api", Commit(daysAgo: 1, totalLines: 100)),
            GivenRepository("acme", "empty"));

        var insights = await WhenQueried();

        insights.Movers.Should().ContainSingle().Which.Name.Should().Be("api");
    }

    // ─── Language mix ────────────────────────────────────────────────────────

    [Fact]
    public async Task LanguageMix_IsRankedAndSumsToOneHundredPercent()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 1, totalLines: 1_000, byFileType: new Dictionary<string, int>
            {
                [".cs"] = 750,
                [".razor"] = 250
            })));

        var insights = await WhenQueried();

        insights.LanguageMix.Select(l => l.Extension).Should().ContainInOrder(".cs", ".razor");
        insights.LanguageMix.Sum(l => l.Percentage).Should().BeApproximately(100, 0.1);
        insights.LanguageMix[0].Lines.Should().Be(750);
    }

    /// <summary>Only the current snapshot's breakdown counts — the mix is "what is there now".</summary>
    [Fact]
    public async Task LanguageMix_ReadsTheNewestSnapshotOnly()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 10, totalLines: 500, byFileType: new Dictionary<string, int> { [".js"] = 500 }),
            Commit(daysAgo: 1, totalLines: 500, byFileType: new Dictionary<string, int> { [".ts"] = 500 })));

        var insights = await WhenQueried();

        insights.LanguageMix.Should().ContainSingle().Which.Extension.Should().Be(".ts");
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

    /// <summary>
    /// A day with no commits *yet* must not read as having broken the streak — it is not over.
    /// This is the case an off-by-one in CurrentStreak silently gets wrong every morning.
    /// </summary>
    [Fact]
    public async Task CurrentStreak_SurvivesATodayWithNoCommitsYet()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 3, totalLines: 100),
            Commit(daysAgo: 2, totalLines: 200),
            Commit(daysAgo: 1, totalLines: 300)));

        var insights = await WhenQueried();

        insights.CurrentStreakDays.Should().Be(3);
    }

    [Fact]
    public async Task CurrentStreak_IsZeroOnceTwoDaysHaveBeenMissed()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 5, totalLines: 100),
            Commit(daysAgo: 4, totalLines: 200)));

        var insights = await WhenQueried();

        insights.CurrentStreakDays.Should().Be(0);
    }

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
        insights.CurrentStreakDays.Should().Be(2);
    }

    [Fact]
    public async Task ActiveDays30_CountsDistinctDays_NotCommits()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 3, totalLines: 100),
            Commit(daysAgo: 3, totalLines: 150),
            Commit(daysAgo: 3, totalLines: 200),
            Commit(daysAgo: 1, totalLines: 250)));

        var insights = await WhenQueried();

        insights.ActiveDays30.Should().Be(2);
        insights.Commits30Days.Should().Be(4);
    }

    /// <summary>
    /// The heatmap draws a fixed grid, so a sparse list would silently shift every cell after the
    /// first gap — a quiet week would redraw the whole year wrong.
    /// </summary>
    [Fact]
    public async Task Activity_IncludesZeroCommitDays_SoTheHeatmapGridStaysAligned()
    {
        GivenRepositories(GivenRepository("acme", "api",
            Commit(daysAgo: 10, totalLines: 100, linesAdded: 40),
            Commit(daysAgo: 1, totalLines: 200, linesAdded: 60)));

        var insights = await WhenQueried();

        insights.Activity.Should().HaveCount(365);
        insights.Activity.Select(a => a.Date).Should().BeInAscendingOrder();
        insights.Activity.Should().OnlyHaveUniqueItems(a => a.Date);
        insights.Activity.Count(a => a.Commits > 0).Should().Be(2);
        insights.Activity.Single(a => a.Date == Today.AddDays(-1)).LinesAdded.Should().Be(60);
    }

    [Fact]
    public async Task Activity_ExcludesCommitsOlderThanTheYearWindow()
    {
        GivenRepositories(GivenRepository("acme", "api", Commit(daysAgo: 400, totalLines: 100, linesAdded: 100)));

        var insights = await WhenQueried();

        insights.Activity.Should().OnlyContain(a => a.Commits == 0);
    }
}
