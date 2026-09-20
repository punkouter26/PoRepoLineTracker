namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Aggregated commit activity for one day-of-week and hour-of-day bucket (7x24 punchcard).
/// </summary>
public sealed class PunchcardItemDto
{
    /// <summary>0 = Sunday, 1 = Monday, ..., 6 = Saturday.</summary>
    public int DayOfWeek { get; set; }

    /// <summary>0 through 23.</summary>
    public int HourOfDay { get; set; }

    public int CommitCount { get; set; }

    public int LinesAdded { get; set; }

    public int LinesRemoved { get; set; }
}

