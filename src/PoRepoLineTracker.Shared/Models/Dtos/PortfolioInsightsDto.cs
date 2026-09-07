using PoRepoLineTracker.Shared.Domain;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Everything the Insights dashboard renders, in one response.
///
/// <para><b>Why a dedicated shape rather than more of /allcharts.</b> The dashboard's figures are
/// aggregates over every commit of every repository — streak lengths, movers, language mix. Doing
/// that in the browser would mean shipping the per-day, per-extension breakdown for the whole
/// portfolio just to reduce it to two dozen numbers. Aggregating where the data already is keeps
/// the payload roughly constant as the number of tracked repositories grows.</para>
///
/// <para>A concrete named type, not an anonymous one, because the wire contract has to be
/// source-generatable.</para>
/// </summary>
public sealed class PortfolioInsightsDto
{
    public int RepositoryCount { get; set; }
    public int AnalyzedCount { get; set; }

    /// <summary>Sum of each repository's most recent total-lines snapshot.</summary>
    public int TotalLines { get; set; }

    /// <summary>Change in <see cref="TotalLines"/> over the window. Negative when code shrank.</summary>
    public int NetLines7Days { get; set; }
    public int NetLines30Days { get; set; }

    public int Commits30Days { get; set; }
    public int LinesAdded30Days { get; set; }

    /// <summary>Consecutive days with at least one commit, counting back from the most recent activity.</summary>
    public int CurrentStreakDays { get; set; }

    /// <summary>Longest such run anywhere in the last year.</summary>
    public int LongestStreakDays { get; set; }

    /// <summary>Days with at least one commit in the last 30.</summary>
    public int ActiveDays30 { get; set; }

    /// <summary>Every repository, ranked by 30-day net change. The page slices gainers off the front and losers off the back.</summary>
    public List<RepositoryMovementDto> Movers { get; set; } = [];

    /// <summary>Current language mix across the portfolio, largest share first.</summary>
    public List<LanguageShareDto> LanguageMix { get; set; } = [];

    /// <summary>
    /// How the language mix moved over the trailing year, ranked by the size of the move, gainers
    /// first. Measured in SHARE, not lines: a share that grew while the language itself shrank means
    /// everything else shrank faster, and a line-count delta cannot show that. Empty when the
    /// portfolio did not exist a year ago — every extension would read "+100%", which is a fact
    /// about the baseline rather than about the year.
    /// </summary>
    public List<LanguageDriftDto> LanguageDrift { get; set; } = [];

    /// <summary>Extension whose share grew most over the window. Null when there is no drift to report.</summary>
    public string? RisingLanguage { get; set; }

    /// <summary>Extension whose share shrank most.</summary>
    public string? FadingLanguage { get; set; }

    /// <summary>One entry per day for the last year, including zero-commit days — the heatmap needs the gaps.</summary>
    public List<ActivityDayDto> Activity { get; set; } = [];

    /// <summary>
    /// Portfolio-wide total lines, sampled roughly monthly over the last year. Each point sums
    /// every repository's <see cref="RepositoryTotals.TotalLinesAsOf"/> at that date — the same
    /// "whole-repository snapshot" definition as <see cref="TotalLines"/>, never a sum of per-commit
    /// deltas.
    /// </summary>
    public List<PortfolioTrendPointDto> TrendLine { get; set; } = [];
}

/// <summary>How one repository moved over the window, for the ranking table.</summary>
public sealed class RepositoryMovementDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int NetChange30Days { get; set; }
    public int Commits30Days { get; set; }

    /// <summary>
    /// Commit count per week for the last 12 weeks, oldest first, most recent week last — feeds the
    /// ranking table's sparkline. Fixed-length so a quiet week reads as zero rather than as absent.
    /// </summary>
    public List<int> WeeklyCommits { get; set; } = [];
}

/// <summary>One sampled point of the portfolio-wide line-count trend.</summary>
public sealed class PortfolioTrendPointDto
{
    public DateTime Date { get; set; }
    public int TotalLines { get; set; }
}

/// <summary>One file extension's share of the portfolio's current lines.</summary>
public sealed class LanguageShareDto
{
    public string Extension { get; set; } = string.Empty;
    public int Lines { get; set; }
    public double Percentage { get; set; }
}

/// <summary>How one extension's share of the portfolio moved between the window's two endpoints.</summary>
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

/// <summary>A single cell of the contribution heatmap.</summary>
public sealed class ActivityDayDto
{
    public DateTime Date { get; set; }
    public int Commits { get; set; }
    public int LinesAdded { get; set; }
}
