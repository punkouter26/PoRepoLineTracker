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
    public async Task NoRepositories_ReturnsAnEmptyShapeRatherThanNulls()
    {
        GivenRepositories();

        var recap = await WhenRecapped();

        recap.HasData.Should().BeFalse();
        recap.Year.Should().Be(Year);
        recap.TopRepos.Should().BeEmpty();
        recap.LanguageDrift.Should().BeEmpty();
        recap.BiggestDay.Should().BeNull();
        // The buckets are still full-length so the page never indexes past the end of a short list.
        recap.CommitsByHour.Should().HaveCount(24);
        recap.CommitsByMonth.Should().HaveCount(12);
        recap.CommitsByWeekday.Should().HaveCount(7);
    }

    /// <summary>
    /// The boundary case the whole page rests on. A commit at the first instant of the year is
    /// INSIDE it; one at the last instant of the previous year is not — and the year-start
    /// baseline is read from the latter.
    /// </summary>
    [Fact]
    public async Task YearBoundaries_AreInclusiveOfJanuaryFirstAndExclusiveOfTheYearBefore()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(Year - 1, 12, 31, 23, 59, 59), totalLines: 1_000, linesAdded: 10),
            Commit(new DateTime(Year, 1, 1, 0, 0, 0), totalLines: 1_100, linesAdded: 100),
            Commit(new DateTime(Year, 12, 31, 23, 59, 59), totalLines: 1_500, linesAdded: 400),
            Commit(new DateTime(Year + 1, 1, 1, 0, 0, 0), totalLines: 9_999, linesAdded: 8_499)));

        var recap = await WhenRecapped();

        recap.Commits.Should().Be(2, "only the two commits inside the year count");
        recap.LinesAdded.Should().Be(500);
        recap.NetGrowth.Should().Be(500, "1,500 at year end minus the 1,000 snapshot carried in");
    }

    /// <summary>
    /// The two figures are measured over different populations, which is why the recap cannot
    /// derive one from the other: LinesAdded/LinesRemoved come from the git diff and cover EVERY
    /// file in the commit, while TotalLines counts only the extensions the user has configured.
    /// A commit that adds 80 lines of documentation moves the churn and not the snapshot.
    /// </summary>
    [Fact]
    public async Task NetGrowth_IsTheSnapshotDifference_NotAddedMinusRemoved()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(1, 10), totalLines: 100, linesAdded: 100),
            // 80 lines of an extension that is not counted: churn moves, the snapshot does not.
            Commit(On(6, 1), totalLines: 100, linesAdded: 80),
            Commit(On(11, 1), totalLines: 150, linesAdded: 50)));

        var recap = await WhenRecapped();

        recap.LinesAdded.Should().Be(230, "churn counts every line the diff touched");
        recap.NetGrowth.Should().Be(150, "the repository did not exist at the start of the year");
        recap.NetGrowth.Should().NotBe(recap.LinesAdded - recap.LinesRemoved,
            "deriving growth from churn would over-count by every uncounted line in the diff");
    }

    [Fact]
    public async Task AvailableYears_ListsOnlyYearsWithCommits_NewestFirst()
    {
        GivenRepositories(
            GivenRepository("me", "old", Commit(On(3, 3, year: 2019), 10)),
            GivenRepository("me", "app", Commit(On(3, 3, year: Year), 10), Commit(On(3, 3, year: 2021), 5)));

        var recap = await WhenRecapped();

        recap.AvailableYears.Should().Equal(Year, 2021, 2019);
    }

    // ─── Standouts ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BiggestDay_IsRankedByLinesAdded_AndAggregatesAcrossRepositories()
    {
        GivenRepositories(
            GivenRepository("me", "a",
                Commit(On(4, 1), 100, linesAdded: 300),
                Commit(On(5, 1), 200, linesAdded: 100)),
            GivenRepository("me", "b",
                Commit(On(5, 1), 50, linesAdded: 900)));

        var recap = await WhenRecapped();

        recap.BiggestDay.Should().NotBeNull();
        recap.BiggestDay!.Date.Should().Be(new DateTime(Year, 5, 1));
        recap.BiggestDay.LinesAdded.Should().Be(1_000, "both repositories' commits on that day count");
        recap.BiggestDay.Commits.Should().Be(2);
    }

    [Fact]
    public async Task ReposStarted_CountsOnlyRepositoriesWhoseFirstEverCommitIsInTheYear()
    {
        GivenRepositories(
            GivenRepository("me", "carried-over",
                Commit(On(6, 1, year: Year - 2), 100),
                Commit(On(6, 1), 200, linesAdded: 100)),
            GivenRepository("me", "brand-new",
                Commit(On(2, 2), 40, linesAdded: 40)));

        var recap = await WhenRecapped();

        recap.ReposTouched.Should().Be(2);
        recap.ReposStarted.Should().Be(1);
        recap.TopRepos.Should().ContainSingle(r => r.Name == "brand-new" && r.StartedThisYear);
    }

    [Fact]
    public async Task BiggestCommits_AreRankedByLinesAdded_AndCarryAShortSha()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(1, 5), 10, linesAdded: 5),
            Commit(On(2, 5), 900, linesAdded: 890),
            Commit(On(3, 5), 950, linesAdded: 50)));

        var recap = await WhenRecapped();

        recap.BiggestCommits.Should().HaveCount(3);
        recap.BiggestCommits[0].LinesAdded.Should().Be(890);
        recap.BiggestCommits[0].Sha.Should().HaveLength(7, "a full 40-character SHA does not fit the card");
    }

    // ─── Rhythm ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeakHourAndWeekday_ComeFromTheCommitTimestampsThemselves()
    {
        // 2023-05-01 is a Monday; 2023-05-03 a Wednesday.
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(5, 1, hour: 23), 10, linesAdded: 1),
            Commit(On(5, 8, hour: 23), 20, linesAdded: 1),
            Commit(On(5, 3, hour: 9), 30, linesAdded: 1)));

        var recap = await WhenRecapped();

        recap.PeakHour.Should().Be(23);
        recap.PeakWeekday.Should().Be(DayOfWeek.Monday);
        recap.CommitsByHour[23].Should().Be(2);
        recap.NightOwlPercent.Should().BeApproximately(66.7, 0.1, "two of three commits landed after 10pm");
    }

    /// <summary>
    /// The weekdays of a window are not equally numerous — 2023 opened on a Sunday, so it holds 53
    /// Sundays against 52 of everything else. Ranking raw totals would hand whichever weekday came
    /// round more often a standing advantage that says nothing about how the user works.
    /// </summary>
    [Fact]
    public async Task WeekdayRanking_UsesTheAveragePerOccurrence_NotTheRawTotal()
    {
        // 2023-01-01 is a Sunday. Three Sundays against three Mondays is a tie on totals, but
        // there are 53 Sundays in 2023 and 52 Mondays — so Monday is the busier weekday.
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(1, 1), 10, linesAdded: 1),   // Sunday
            Commit(On(1, 8), 20, linesAdded: 1),   // Sunday
            Commit(On(1, 15), 30, linesAdded: 1),  // Sunday
            Commit(On(1, 2), 40, linesAdded: 1),   // Monday
            Commit(On(1, 9), 50, linesAdded: 1),   // Monday
            Commit(On(1, 16), 60, linesAdded: 1))); // Monday

        var recap = await WhenRecapped();

        recap.WeekdayOccurrences[(int)DayOfWeek.Sunday].Should().Be(53);
        recap.WeekdayOccurrences[(int)DayOfWeek.Monday].Should().Be(52);

        recap.CommitsByWeekday[(int)DayOfWeek.Sunday]
            .Should().Be(recap.CommitsByWeekday[(int)DayOfWeek.Monday], "the raw totals are tied");

        recap.PeakWeekday.Should().Be(DayOfWeek.Monday, "the same commits spread over fewer Mondays is a higher average");

        // The displayed averages round to the same 0.06 here — which is exactly why the RANKING
        // has to run on the unrounded values.
        recap.AverageCommitsByWeekday[(int)DayOfWeek.Sunday]
            .Should().Be(recap.AverageCommitsByWeekday[(int)DayOfWeek.Monday]);
    }

    [Fact]
    public async Task WeekdayOccurrences_AreBoundedByTodayInAPartialYear()
    {
        var now = DateTime.UtcNow;
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(now.Year, 1, 2), totalLines: 10, linesAdded: 10)));

        var recap = await WhenRecapped(year: null);

        // A year in progress must not be credited with weekdays that have not happened yet —
        // otherwise every average is quietly divided by a full year's worth of them.
        recap.WeekdayOccurrences.Sum().Should().Be(now.DayOfYear,
            "the window runs from 1 January to today, inclusive");
    }

    [Fact]
    public async Task LongestStreak_IsBoundedByTheYearItself()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(7, 10), 10, linesAdded: 1),
            Commit(On(7, 11), 20, linesAdded: 1),
            Commit(On(7, 12), 30, linesAdded: 1),
            Commit(On(9, 1), 40, linesAdded: 1)));

        var recap = await WhenRecapped();

        recap.ActiveDays.Should().Be(4);
        recap.LongestStreakDays.Should().Be(3);
    }

    // ─── Language drift ──────────────────────────────────────────────────────

    /// <summary>
    /// The recap's one figure that is not available anywhere else, and the one most easily got
    /// wrong: a share can rise while the language itself shrinks, because everything around it
    /// shrank faster.
    /// </summary>
    [Fact]
    public async Task LanguageDrift_RanksByShareMoved_NotByLineCount()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(Year - 1, 12, 1), totalLines: 2_000,
                byFileType: new Dictionary<string, int> { [".cs"] = 1_000, [".js"] = 1_000 }),
            Commit(On(12, 1), totalLines: 2_000, linesAdded: 1_000,
                byFileType: new Dictionary<string, int> { [".cs"] = 1_800, [".js"] = 200 })));

        var recap = await WhenRecapped();

        recap.RisingLanguage.Should().Be(".cs");
        recap.FadingLanguage.Should().Be(".js");

        var cs = recap.LanguageDrift.Single(d => d.Extension == ".cs");
        cs.StartPercent.Should().Be(50);
        cs.EndPercent.Should().Be(90);
        cs.PercentDelta.Should().Be(40);
    }

    [Fact]
    public async Task LanguageDrift_IgnoresTrivialExtensions()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(Year - 1, 12, 1), totalLines: 1_003,
                byFileType: new Dictionary<string, int> { [".cs"] = 1_000, [".yml"] = 3 }),
            Commit(On(12, 1), totalLines: 1_009, linesAdded: 6,
                byFileType: new Dictionary<string, int> { [".cs"] = 1_000, [".yml"] = 9 })));

        var recap = await WhenRecapped();

        recap.LanguageDrift.Should().NotContain(d => d.Extension == ".yml",
            "a file type that went from 3 lines to 9 says nothing about how the year was spent");
    }

    /// <summary>
    /// A steady year is the common case for a mature portfolio, and it used to produce a "what you
    /// drifted toward" card whose every row read "70% → 70%, +0.0 pts" under an empty sentence —
    /// there was no rising or fading language for the sentence to name.
    /// </summary>
    [Fact]
    public async Task LanguageDrift_IsEmpty_WhenNothingActuallyMoved()
    {
        var mix = new Dictionary<string, int> { [".cs"] = 7_000, [".razor"] = 3_000 };
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(Year - 1, 12, 1), totalLines: 10_000, byFileType: mix),
            // Twice the size, identical proportions: lines moved, share did not.
            Commit(On(11, 1), totalLines: 20_000, linesAdded: 10_000,
                byFileType: new Dictionary<string, int> { [".cs"] = 14_000, [".razor"] = 6_000 })));

        var recap = await WhenRecapped();

        recap.NetGrowth.Should().Be(10_000, "the portfolio did grow — it just did not drift");
        recap.LanguageDrift.Should().BeEmpty("a language holding exactly its share is not drift");
        recap.RisingLanguage.Should().BeNull();
        recap.FadingLanguage.Should().BeNull();
    }

    /// <summary>
    /// A steady year can leave every day tied on lines and commits, and the winner was then
    /// whatever order the dictionary happened to enumerate in — not guaranteed, and observed to
    /// differ between runs over identical data.
    /// </summary>
    [Fact]
    public async Task BiggestDay_BreaksATieOnTheDate_SoTheAnswerIsStable()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(3, 1), 100, linesAdded: 50),
            Commit(On(6, 1), 150, linesAdded: 50),
            Commit(On(9, 1), 200, linesAdded: 50)));

        var recap = await WhenRecapped();

        recap.BiggestDay!.Date.Should().Be(new DateTime(Year, 9, 1),
            "the most recent of equals is both stable and the more interesting one to be told about");

        // Broken the SAME way, so the two never disagree over one tie — the page renders them
        // together, and a September biggest day above a March biggest commit reads as a bug.
        recap.BiggestCommits[0].Date.Date.Should().Be(new DateTime(Year, 9, 1));
    }

    [Fact]
    public async Task LanguageDrift_IsEmpty_WhenThePortfolioDidNotExistAtTheStartOfTheYear()
    {
        GivenRepositories(GivenRepository("me", "app",
            Commit(On(2, 1), totalLines: 5_000, linesAdded: 5_000,
                byFileType: new Dictionary<string, int> { [".cs"] = 5_000 })));

        var recap = await WhenRecapped();

        recap.HasData.Should().BeTrue();
        recap.LanguageDrift.Should().BeEmpty("every extension would read as +100%, which is a fact about the baseline");
        recap.RisingLanguage.Should().BeNull();
    }

    // ─── Current year ────────────────────────────────────────────────────────

    [Fact]
    public async Task NullYear_ResolvesToTheCurrentOne_AndIsFlaggedPartial()
    {
        var thisYear = DateTime.UtcNow.Year;
        GivenRepositories(GivenRepository("me", "app",
            Commit(new DateTime(thisYear, 1, 2), totalLines: 10, linesAdded: 10)));

        var recap = await WhenRecapped(year: null);

        recap.Year.Should().Be(thisYear);
        recap.IsPartialYear.Should().BeTrue();
    }
}
