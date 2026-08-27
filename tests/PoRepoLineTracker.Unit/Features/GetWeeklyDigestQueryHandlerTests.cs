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

    private void GivenRepositories(params GitHubRepository[] repositories) =>
        _dataService.GetAllRepositoriesAsync(_userId).Returns(repositories.ToList());

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

    [Fact]
    public async Task NoLastVisit_FallsBackToATrailingWeek()
    {
        GivenRepositories(GivenRepository("me", "app", Commit(hoursAgo: 24, 100, linesAdded: 50)));

        var digest = await WhenDigested(lastSeen: null);

        digest.IsSinceLastVisit.Should().BeFalse();
        digest.LastVisitUtc.Should().BeNull();
        (digest.UntilUtc - digest.SinceUtc).TotalDays.Should().BeApproximately(7, 0.01);
    }

    /// <summary>
    /// The case that makes the feature look broken if it is got wrong: someone who reloaded twenty
    /// minutes ago has no news, and "0 commits since you were last here" is worse than no banner.
    /// </summary>
    [Fact]
    public async Task AVeryRecentVisit_IsIgnoredInFavourOfTheWeek()
    {
        GivenRepositories(GivenRepository("me", "app", Commit(hoursAgo: 48, 100, linesAdded: 50)));

        var digest = await WhenDigested(lastSeen: Now.AddMinutes(-20));

        digest.IsSinceLastVisit.Should().BeFalse();
        digest.LastVisitUtc.Should().NotBeNull("the timestamp is still reported even when it is not used as the window");
        digest.Commits.Should().Be(1, "the week window still contains the two-day-old commit");
    }

    [Fact]
    public async Task AVisitFromYesterday_IsUsedAsTheWindow()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(hoursAgo: 10, 200, linesAdded: 100),
            Commit(hoursAgo: 100, 100, linesAdded: 100)));

        var lastSeen = Now.AddHours(-30);
        var digest = await WhenDigested(lastSeen);

        digest.IsSinceLastVisit.Should().BeTrue();
        digest.SinceUtc.Should().Be(lastSeen);
        digest.Commits.Should().Be(1, "only the commit inside the 30-hour window counts");
    }

    [Fact]
    public async Task AVeryOldVisit_IsIgnored_SoTheBannerDoesNotReportAYearAsNews()
    {
        GivenRepositories(GivenRepository("me", "app", Commit(hoursAgo: 12, 100, linesAdded: 5)));

        var digest = await WhenDigested(lastSeen: Now.AddDays(-400));

        digest.IsSinceLastVisit.Should().BeFalse();
        (digest.UntilUtc - digest.SinceUtc).TotalDays.Should().BeApproximately(7, 0.01);
    }

    [Fact]
    public async Task AVisitInTheFuture_IsIgnoredRatherThanProducingANegativeWindow()
    {
        GivenRepositories(GivenRepository("me", "app", Commit(hoursAgo: 12, 100, linesAdded: 5)));

        var digest = await WhenDigested(lastSeen: Now.AddHours(6));

        digest.IsSinceLastVisit.Should().BeFalse();
        digest.SinceUtc.Should().BeBefore(digest.UntilUtc);
    }

    // ─── Figures ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoRepositories_ReportsNoActivityRatherThanAWallOfZeroes()
    {
        GivenRepositories();

        var digest = await WhenDigested();

        digest.HasActivity.Should().BeFalse();
        digest.TopRepos.Should().BeEmpty();
    }

    [Fact]
    public async Task AQuietWindow_ReportsNoActivity()
    {
        // The only commit predates the window, and the snapshot has not moved inside it.
        GivenRepositories(GivenRepository("me", "app", Commit(hoursAgo: 24 * 30, 100, linesAdded: 100)));

        var digest = await WhenDigested();

        digest.Commits.Should().Be(0);
        digest.NetGrowth.Should().Be(0);
        digest.HasActivity.Should().BeFalse();
    }

    [Fact]
    public async Task NetGrowth_IsTheSnapshotDifferenceAcrossTheWindow()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(hoursAgo: 24 * 20, totalLines: 1_000, linesAdded: 1_000),
            Commit(hoursAgo: 24, totalLines: 1_250, linesAdded: 400, linesRemoved: 150)));

        var digest = await WhenDigested();

        digest.LinesAdded.Should().Be(400);
        digest.LinesRemoved.Should().Be(150);
        digest.NetGrowth.Should().Be(250, "1,250 now against the 1,000 snapshot carried into the window");
    }

    [Fact]
    public async Task PreviousWindow_IsTheEquallyLongWindowImmediatelyBefore()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(hoursAgo: 24, 400, linesAdded: 100),
            Commit(hoursAgo: 48, 300, linesAdded: 100),
            // Inside the preceding 7 days, so it lands in the comparison rather than the window.
            Commit(hoursAgo: 24 * 9, 200, linesAdded: 70),
            // Older than both windows.
            Commit(hoursAgo: 24 * 30, 100, linesAdded: 100)));

        var digest = await WhenDigested();

        digest.Commits.Should().Be(2);
        digest.PreviousCommits.Should().Be(1);
        digest.PreviousLinesAdded.Should().Be(70);
    }

    [Fact]
    public async Task TopRepos_AreRankedByCommits_AndOnlyIncludeOnesTouchedInTheWindow()
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

        var digest = await WhenDigested();

        digest.ReposTouched.Should().Be(2);
        digest.TopRepos.Should().HaveCount(2);
        digest.TopRepos[0].Name.Should().Be("busy");
        digest.TopRepos.Should().NotContain(r => r.Name == "dormant");
    }

    /// <summary>
    /// Growth without commits is real: re-analysing with a different counted-extension list moves
    /// a repository's size with no new commit behind it, and that is exactly the change a
    /// returning user wants flagged.
    /// </summary>
    [Fact]
    public async Task ActiveDays_CountsDistinctDays_NotCommits()
    {
        // Two commits at the SAME instant, not merely a close one. Offsets of 2 and 3 hours land
        // on the same calendar day for most of the day and straddle midnight for the rest, so a
        // test written that way passes or fails depending on the hour it is run at — which is a
        // flake, not a check. Identical timestamps are the same day at every hour; the third
        // commit is three days out, so it cannot collide either.
        GivenRepositories(GivenRepository("me", "app",
            Commit(hoursAgo: 2, 100, linesAdded: 1),
            Commit(hoursAgo: 2, 90, linesAdded: 1),
            Commit(hoursAgo: 24 * 3, 80, linesAdded: 1)));

        var digest = await WhenDigested();

        digest.Commits.Should().Be(3);
        digest.ActiveDays.Should().Be(2);
    }
}
