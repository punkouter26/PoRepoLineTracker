using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// One calendar year of the user's portfolio, reduced to the handful of figures the /recap page
/// reveals a card at a time.
///
/// <para><b>Why a separate shape from <see cref="PortfolioInsightsDto"/>.</b> Insights answers
/// "how is my code doing right now" — its windows are trailing (7/30/365 days from today) and its
/// figures move every day. A recap answers "what did <i>this year</i> look like", so every window
/// is a fixed calendar boundary and the same request returns the same answer forever once the year
/// is over. Sharing one DTO would mean one of the two carrying windows it does not use.</para>
///
/// <para>Every figure here is derived from stored commits only. There is no inference, no model
/// and no scoring — see CLAUDE.md.</para>
/// </summary>
public sealed class YearInCodeDto
{
    public int Year { get; set; }

    /// <summary>
    /// False when the year contains no commits at all. The page renders an "nothing to recap"
    /// state rather than a wall of zeroes, which reads as a broken query.
    /// </summary>
    public bool HasData { get; set; }

    /// <summary>True while <see cref="Year"/> is the current one — the page labels it "so far".</summary>
    public bool IsPartialYear { get; set; }

    /// <summary>Years the user actually has commits in, newest first. Drives the year picker.</summary>
    public List<int> AvailableYears { get; set; } = [];

    // ── Headline ────────────────────────────────────────────────────────────────────────────

    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }

    /// <summary>
    /// Portfolio size at the end of the year minus its size at the start, over the repositories
    /// tracked today. Not <c>LinesAdded - LinesRemoved</c>: those are diff churn and count a line
    /// that was written and rewritten twice, where this is the difference between two
    /// whole-repository snapshots (see <see cref="RepositoryTotals"/>).
    /// </summary>
    public int NetGrowth { get; set; }

    public int Commits { get; set; }

    /// <summary>Distinct days with at least one commit.</summary>
    public int ActiveDays { get; set; }

    /// <summary>Longest run of consecutive active days that lies inside the year.</summary>
    public int LongestStreakDays { get; set; }

    /// <summary>Repositories with at least one commit in the year.</summary>
    public int ReposTouched { get; set; }

    /// <summary>Repositories whose very first commit falls inside the year.</summary>
    public int ReposStarted { get; set; }

    // ── Standouts ───────────────────────────────────────────────────────────────────────────

    /// <summary>The single day with the most lines added. Null when the year has no commits.</summary>
    public RecapDayDto? BiggestDay { get; set; }

    /// <summary>Repositories ranked by net growth over the year, largest first.</summary>
    public List<RecapRepoDto> TopRepos { get; set; } = [];

    /// <summary>The largest individual commits of the year by lines added.</summary>
    public List<RecapCommitDto> BiggestCommits { get; set; } = [];

    // ── Rhythm ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Commit counts for hours 0–23, in the commit timestamps' own offset.</summary>
    public List<int> CommitsByHour { get; set; } = [];

    /// <summary>Commit counts for the 12 months of the year, January first.</summary>
    public List<int> CommitsByMonth { get; set; } = [];

    /// <summary>Commit counts for the seven weekdays, Sunday first — index matches <see cref="DayOfWeek"/>.</summary>
    public List<int> CommitsByWeekday { get; set; } = [];

    /// <summary>
    /// How many times each weekday actually occurred inside the measured window, Sunday first.
    ///
    /// <para>Present because the weekdays of a window are not equally numerous. A year in progress
    /// that ends on a Wednesday has had one more Monday than Friday, and a full year has 53 of
    /// whichever weekday it starts on. Ranking weekdays by raw totals therefore hands a standing
    /// advantage to whichever days happen to have come round more often — which is a fact about
    /// the calendar, not about how the user works.</para>
    /// </summary>
    public List<int> WeekdayOccurrences { get; set; } = [];

    /// <summary>
    /// <see cref="CommitsByWeekday"/> divided by <see cref="WeekdayOccurrences"/> — commits per
    /// occurrence of that weekday. This is the figure the recap ranks and the chart plots.
    /// </summary>
    public List<double> AverageCommitsByWeekday { get; set; } = [];

    /// <summary>Hour of day the user commits in most, 0–23.</summary>
    public int PeakHour { get; set; }

    /// <summary>
    /// The weekday with the highest AVERAGE commit count — see <see cref="WeekdayOccurrences"/>
    /// for why the average and not the total.
    /// </summary>
    public DayOfWeek PeakWeekday { get; set; }

    /// <summary>Commits per occurrence of <see cref="PeakWeekday"/>, so the page can state the figure it ranked on.</summary>
    public double PeakWeekdayAverage { get; set; }

    /// <summary>Share of commits landing between 22:00 and 05:00, as a percentage.</summary>
    public double NightOwlPercent { get; set; }

    // ── Language drift ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// How the portfolio's language mix moved across the year, ranked by the size of the move.
    /// This is the recap's one genuinely non-obvious figure: a share that grew while the language
    /// itself shrank means everything else shrank faster.
    /// </summary>
    public List<LanguageDriftDto> LanguageDrift { get; set; } = [];

    /// <summary>Extension whose share of the portfolio grew most. Null when the year has no drift to report.</summary>
    public string? RisingLanguage { get; set; }

    /// <summary>Extension whose share shrank most.</summary>
    public string? FadingLanguage { get; set; }
}

/// <summary>The year's single most productive day.</summary>
public sealed class RecapDayDto
{
    public DateTime Date { get; set; }
    public int Commits { get; set; }
    public int LinesAdded { get; set; }
}

/// <summary>One repository's year, for the recap's ranking card.</summary>
public sealed class RecapRepoDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Commits { get; set; }
    public int LinesAdded { get; set; }

    /// <summary>Snapshot at year end minus snapshot at year start — same definition as <see cref="YearInCodeDto.NetGrowth"/>.</summary>
    public int NetGrowth { get; set; }

    /// <summary>True when the repository's first commit is inside the year.</summary>
    public bool StartedThisYear { get; set; }
}

/// <summary>One of the year's largest commits.</summary>
public sealed class RecapCommitDto
{
    /// <summary>Short SHA — commit messages are not stored, so this is the only identity the recap can print.</summary>
    public string Sha { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
}

/// <summary>How one extension's share of the portfolio moved between the year's two endpoints.</summary>
public sealed class LanguageDriftDto
{
    public string Extension { get; set; } = string.Empty;
    public int StartLines { get; set; }
    public int EndLines { get; set; }
    public double StartPercent { get; set; }
    public double EndPercent { get; set; }

    /// <summary>End share minus start share, in percentage points. Negative when the language lost ground.</summary>
    public double PercentDelta { get; set; }
}
