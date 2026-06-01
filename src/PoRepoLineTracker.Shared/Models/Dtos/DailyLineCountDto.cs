namespace PoRepoLineTracker.Shared.Models.Dtos;

public class DailyLineCountDto
{
    public DateTime Date { get; set; }
    public int TotalLines { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesDeleted { get; set; }
    public int TotalLinesChanged { get; set; }
    public Dictionary<string, int> LinesByFileType { get; set; } = new();
    public int CommitCount { get; set; }

    /// <summary>
    /// CommitTagger: Aggregated tags from all commits on this day.
    /// Used by InstantReplay timeline scrubber to show tag badges per day.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Average AI percentage across commits on this day.
    /// Used by InstantReplay to show AI % at any point in time.
    /// </summary>
    public double AverageAiPercentage { get; set; }
}
