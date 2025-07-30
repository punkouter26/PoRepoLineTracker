namespace PoRepoLineTracker.Application.Models;

public class DailyLineCountDto
{
    public DateTime Date { get; set; }
    public int TotalLines { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesDeleted { get; set; }
    public int TotalLinesChanged { get; set; }
    public Dictionary<string, int> LinesByFileType { get; set; } = new();
    public int CommitCount { get; set; }
}
