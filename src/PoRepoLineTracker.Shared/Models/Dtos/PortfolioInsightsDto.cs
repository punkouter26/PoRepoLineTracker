using PoRepoLineTracker.Domain.Models;

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
/// source-generatable (Rule 1.2).</para>
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

    /// <summary>Lines-weighted AI share across the last 30 days, 0–100.</summary>
    public double AiPercentage30Days { get; set; }

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

    /// <summary>One entry per day for the last year, including zero-commit days — the heatmap needs the gaps.</summary>
    public List<ActivityDayDto> Activity { get; set; } = [];
}

/// <summary>How one repository moved over the window, for the gainers/losers table.</summary>
public sealed class RepositoryMovementDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int NetChange30Days { get; set; }
    public int Commits30Days { get; set; }
    public double AiPercentage30Days { get; set; }
}

/// <summary>One file extension's share of the portfolio's current lines.</summary>
public sealed class LanguageShareDto
{
    public string Extension { get; set; } = string.Empty;
    public int Lines { get; set; }
    public double Percentage { get; set; }
}

/// <summary>A single cell of the contribution heatmap.</summary>
public sealed class ActivityDayDto
{
    public DateTime Date { get; set; }
    public int Commits { get; set; }
    public int LinesAdded { get; set; }
}
