using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Features.Recap;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The recap is arithmetic over a FIXED calendar window, which is a different class of bug from
/// the dashboard's trailing windows: an off-by-one at a year boundary silently moves a commit into
/// the wrong year, and nothing on the page looks wrong when it does. These pin the boundaries, the
/// snapshot-versus-churn distinction, and the language-drift ranking.
///
/// <para>Exercised through <c>Handle</c> rather than the private helpers, so the assertions are on
/// the figures the page actually renders.</para>
/// </summary>
public class GetYearInCodeQueryHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly GetYearInCodeQueryHandler _sut;
    private readonly UserId _userId = UserId.New();

    /// <summary>
    /// A year that is definitively over, so no test depends on where in the current year it runs.
    /// The partial-year path is covered separately with <see cref="DateTime.UtcNow"/>'s own year.
    /// </summary>
    private const int Year = 2023;

    public GetYearInCodeQueryHandlerTests()
    {
        _sut = new GetYearInCodeQueryHandler(
            _dataService, Substitute.For<ILogger<GetYearInCodeQueryHandler>>());
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

    private static CommitLineCount Commit(
        DateTime date,
        int totalLines,
        int linesAdded = 0,
        int linesRemoved = 0,
        Dictionary<string, int>? byFileType = null) => new()
        {
            Id = Guid.NewGuid(),
            CommitSha = Guid.NewGuid().ToString("N"),
            CommitDate = date,
            TotalLines = totalLines,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            LinesByFileType = byFileType ?? []
        };

    private static DateTime On(int month, int day, int hour = 12, int year = Year) => new(year, month, day, hour, 0, 0);

    private Task<YearInCodeDto> WhenRecapped(int? year = Year) =>
        _sut.Handle(new GetYearInCodeQuery(_userId, year), CancellationToken.None);

    // ─── Shape and boundaries ────────────────────────────────────────────────

    [Fact]
    public async Task NoRepositories_ReturnsEmptyShape_AndAvailableYearsHandled()
    {
        GivenRepositories();
        var recap = await WhenRecapped();

        recap.HasData.Should().BeFalse();
        recap.Year.Should().Be(Year);
        recap.TopRepos.Should().BeEmpty();
        recap.CommitsByHour.Should().HaveCount(24);

        GivenRepositories(
            GivenRepository("me", "old", Commit(On(3, 3, year: 2019), 10)),
            GivenRepository("me", "app", Commit(On(3, 3, year: Year), 10), Commit(On(3, 3, year: 2021), 5)));

        var recapWithYears = await WhenRecapped();
        recapWithYears.AvailableYears.Should().Equal(Year, 2021, 2019);
    }

    [Fact]
    public async Task YearBoundaries_AndNetGrowthSnapshot_AreCalculatedCorrectly()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(Year - 1, 12, 31, 23, 59, 59), totalLines: 1_000, linesAdded: 10),
            Commit(new DateTime(Year, 1, 1, 0, 0, 0), totalLines: 1_100, linesAdded: 100),
            Commit(new DateTime(Year, 12, 31, 23, 59, 59), totalLines: 1_500, linesAdded: 400),
            Commit(new DateTime(Year + 1, 1, 1, 0, 0, 0), totalLines: 9_999, linesAdded: 8_499)));

        var recap = await WhenRecapped();
        recap.Commits.Should().Be(2);
        recap.LinesAdded.Should().Be(500);
        recap.NetGrowth.Should().Be(500);

        var thisYear = DateTime.UtcNow.Year;
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(thisYear, 1, 2), totalLines: 10, linesAdded: 10)));
        var partialRecap = await WhenRecapped(year: null);
        partialRecap.Year.Should().Be(thisYear);
        partialRecap.IsPartialYear.Should().BeTrue();
    }

    [Fact]
    public async Task Standouts_BiggestDayAndCommitsAndReposStarted_AreRanked()
    {
        GivenRepositories(
            GivenRepository("me", "carried-over",
                Commit(On(6, 1, year: Year - 2), 100),
                Commit(On(6, 1), 200, linesAdded: 100)),
            GivenRepository("me", "brand-new",
                Commit(On(2, 2), 40, linesAdded: 40)),
            GivenRepository("me", "b",
                Commit(On(6, 1), 50, linesAdded: 900)));

        var recap = await WhenRecapped();

        recap.ReposTouched.Should().Be(3);
        recap.ReposStarted.Should().Be(2);
        recap.BiggestDay.Should().NotBeNull();
        recap.BiggestDay!.Date.Should().Be(new DateTime(Year, 6, 1));
        recap.BiggestDay.LinesAdded.Should().Be(1_000);
        recap.BiggestCommits.Should().NotBeEmpty();
        recap.BiggestCommits[0].LinesAdded.Should().Be(900);
        recap.BiggestCommits[0].Sha.Should().HaveLength(7);
    }

    [Fact]
    public async Task Rhythm_PeakHourAndWeekday_CalculatedByAverages()
    {
        // 2023-01-01 is a Sunday (53 occurrences), 2023-01-02 is a Monday (52 occurrences)
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(1, 1, hour: 23), 10, linesAdded: 1),
            Commit(On(1, 8, hour: 23), 20, linesAdded: 1),
            Commit(On(1, 15, hour: 23), 30, linesAdded: 1),
            Commit(On(1, 2, hour: 9), 40, linesAdded: 1),
            Commit(On(1, 9, hour: 9), 50, linesAdded: 1),
            Commit(On(1, 16, hour: 9), 60, linesAdded: 1)));

        var recap = await WhenRecapped();

        recap.PeakHour.Should().BeOneOf(9, 23);
        recap.WeekdayOccurrences[(int)DayOfWeek.Sunday].Should().Be(53);
        recap.WeekdayOccurrences[(int)DayOfWeek.Monday].Should().Be(52);
        recap.PeakWeekday.Should().Be(DayOfWeek.Monday);
        recap.NightOwlPercent.Should().BeApproximately(50.0, 0.1);
    }

    [Fact]
    public async Task Rhythm_StreakAndPartialYearOccurrences()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(7, 10), 10, linesAdded: 1),
            Commit(On(7, 11), 20, linesAdded: 1),
            Commit(On(7, 12), 30, linesAdded: 1),
            Commit(On(9, 1), 40, linesAdded: 1)));

        var recap = await WhenRecapped();
        recap.ActiveDays.Should().Be(4);
        recap.LongestStreakDays.Should().Be(3);

        var now = DateTime.UtcNow;
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(now.Year, 1, 2), totalLines: 10, linesAdded: 10)));
        var partialRecap = await WhenRecapped(year: null);
        partialRecap.WeekdayOccurrences.Sum().Should().Be(now.DayOfYear);
    }

    [Fact]
    public async Task LanguageDrift_CalculatedByShareMoved_AndIgnoresTrivialOrStatic()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(Year - 1, 12, 1), totalLines: 2_000,
                byFileType: new Dictionary<string, int> { [".cs"] = 1_000, [".js"] = 1_000, [".yml"] = 3 }),
            Commit(On(12, 1), totalLines: 2_000, linesAdded: 1_000,
                byFileType: new Dictionary<string, int> { [".cs"] = 1_800, [".js"] = 200, [".yml"] = 9 })));

        var recap = await WhenRecapped();
        recap.RisingLanguage.Should().Be(".cs");
        recap.FadingLanguage.Should().Be(".js");
        recap.LanguageDrift.Should().NotContain(d => d.Extension == ".yml");

        var cs = recap.LanguageDrift.Single(d => d.Extension == ".cs");
        cs.StartPercent.Should().BeApproximately(50, 0.5);
        cs.EndPercent.Should().BeApproximately(90, 0.5);
        cs.PercentDelta.Should().BeApproximately(40, 0.5);
    }
}
