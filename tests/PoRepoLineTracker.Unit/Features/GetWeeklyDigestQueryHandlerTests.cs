using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Features.Insights;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The digest's whole behaviour is its window, and the window is chosen by the handler rather than
/// by the caller. Everything worth breaking lives in that choice: a last visit that is too recent
/// produces a banner announcing nothing, one that is too old produces a year of "news", and either
/// makes the feature look broken on the page it is meant to make worth returning to.
/// </summary>
public class GetWeeklyDigestQueryHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly GetWeeklyDigestQueryHandler _sut;
    private readonly UserId _userId = UserId.New();

    private static readonly DateTime Now = DateTime.UtcNow;

    public GetWeeklyDigestQueryHandlerTests()
    {
        _sut = new GetWeeklyDigestQueryHandler(
            _dataService, Substitute.For<ILogger<GetWeeklyDigestQueryHandler>>());
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

    private void GivenRepositories(params GitHubRepository[] repositories)
    {
        _dataService.GetAllRepositoriesAsync(_userId).Returns(repositories.ToList());

        // Same lifted fan-out the production handler now uses — see GetPortfolioInsights tests.
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

    private static CommitLineCount Commit(double hoursAgo, int totalLines, int linesAdded = 0, int linesRemoved = 0) => new()
    {
        Id = Guid.NewGuid(),
        CommitSha = Guid.NewGuid().ToString("N")[..7],
        CommitDate = Now.AddHours(-hoursAgo),
        TotalLines = totalLines,
        LinesAdded = linesAdded,
        LinesRemoved = linesRemoved,
        LinesByFileType = []
    };

    private Task<WeeklyDigestDto> WhenDigested(DateTime? lastSeen = null) =>
        _sut.Handle(new GetWeeklyDigestQuery(_userId, lastSeen), CancellationToken.None);

    // ─── Window selection ────────────────────────────────────────────────────

    // ─── Window selection ────────────────────────────────────────────────────

    [Fact]
    public async Task WindowSelection_HandlesRecentOldAndFutureVisits_FallingBackToTrailingWeek()
    {
        // 1. No last visit
        GivenRepositories(GivenRepository("me", "app1", Commit(hoursAgo: 24, 100, linesAdded: 50)));
        var digest1 = await WhenDigested(lastSeen: null);
        digest1.IsSinceLastVisit.Should().BeFalse();
        digest1.LastVisitUtc.Should().BeNull();
        (digest1.UntilUtc - digest1.SinceUtc).TotalDays.Should().BeApproximately(7, 0.01);

        // 2. Very recent visit
        GivenRepositories(GivenRepository("me", "app2", Commit(hoursAgo: 48, 100, linesAdded: 50)));
        var digest2 = await WhenDigested(lastSeen: Now.AddMinutes(-20));
        digest2.IsSinceLastVisit.Should().BeFalse();
        digest2.LastVisitUtc.Should().NotBeNull();
        digest2.Commits.Should().Be(1);

        // 3. Very old visit
        GivenRepositories(GivenRepository("me", "app3", Commit(hoursAgo: 12, 100, linesAdded: 5)));
        var digest3 = await WhenDigested(lastSeen: Now.AddDays(-400));
        digest3.IsSinceLastVisit.Should().BeFalse();
        (digest3.UntilUtc - digest3.SinceUtc).TotalDays.Should().BeApproximately(7, 0.01);

        // 4. Future visit
        var digest4 = await WhenDigested(lastSeen: Now.AddHours(6));
        digest4.IsSinceLastVisit.Should().BeFalse();
        digest4.SinceUtc.Should().BeBefore(digest4.UntilUtc);
    }

    [Fact]
    public async Task WindowSelection_UsesValidYesterdayVisit()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(hoursAgo: 10, 200, linesAdded: 100),
            Commit(hoursAgo: 100, 100, linesAdded: 100)));

        var lastSeen = Now.AddHours(-30);
        var digest = await WhenDigested(lastSeen);

        digest.IsSinceLastVisit.Should().BeTrue();
        digest.SinceUtc.Should().Be(lastSeen);
        digest.Commits.Should().Be(1);
    }

    // ─── Figures ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Figures_HandlesEmptyOrQuietRepositories()
    {
        GivenRepositories();
        var empty = await WhenDigested();
        empty.HasActivity.Should().BeFalse();
        empty.TopRepos.Should().BeEmpty();

        GivenRepositories(GivenRepository("me", "quiet", Commit(hoursAgo: 24 * 30, 100, linesAdded: 100)));
        var quiet = await WhenDigested();
        quiet.Commits.Should().Be(0);
        quiet.NetGrowth.Should().Be(0);
        quiet.HasActivity.Should().BeFalse();
    }

    [Fact]
    public async Task Figures_CalculatesNetGrowthAndPreviousWindow()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(hoursAgo: 24 * 20, totalLines: 1_000, linesAdded: 1_000),
            Commit(hoursAgo: 24, totalLines: 1_250, linesAdded: 400, linesRemoved: 150)));

        var growthDigest = await WhenDigested();
        growthDigest.LinesAdded.Should().Be(400);
        growthDigest.LinesRemoved.Should().Be(150);
        growthDigest.NetGrowth.Should().Be(250);

        GivenRepositories(GivenRepository("me", "app2",
            Commit(hoursAgo: 24, 400, linesAdded: 100),
            Commit(hoursAgo: 48, 300, linesAdded: 100),
            Commit(hoursAgo: 24 * 9, 200, linesAdded: 70),
            Commit(hoursAgo: 24 * 30, 100, linesAdded: 100)));

        var prevDigest = await WhenDigested();
        prevDigest.Commits.Should().Be(2);
        prevDigest.PreviousCommits.Should().Be(1);
        prevDigest.PreviousLinesAdded.Should().Be(70);
    }

    [Fact]
    public async Task Figures_RanksTopReposAndCountsActiveDays()
    {
        GivenRepositories(
            GivenRepository("me", "busy",
                Commit(hoursAgo: 5, 300, linesAdded: 10),
                Commit(hoursAgo: 6, 290, linesAdded: 10),
                Commit(hoursAgo: 7, 280, linesAdded: 10)),
            GivenRepository("me", "quiet",
                Commit(hoursAgo: 8, 100, linesAdded: 90)),
            GivenRepository("me", "dormant",
                Commit(hoursAgo: 24 * 60, 500, linesAdded: 500)));

        var topDigest = await WhenDigested();
        topDigest.ReposTouched.Should().Be(2);
        topDigest.TopRepos.Should().HaveCount(2);
        topDigest.TopRepos[0].Name.Should().Be("busy");
        topDigest.TopRepos.Should().NotContain(r => r.Name == "dormant");

        GivenRepositories(GivenRepository("me", "active",
            Commit(hoursAgo: 2, 100, linesAdded: 1),
            Commit(hoursAgo: 2, 90, linesAdded: 1),
            Commit(hoursAgo: 24 * 3, 80, linesAdded: 1)));

        var activeDigest = await WhenDigested();
        activeDigest.Commits.Should().Be(3);
        activeDigest.ActiveDays.Should().Be(2);
    }
}
