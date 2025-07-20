namespace PoRepoLineTracker.Application.Models;

public class DailyLineCountDto
{
    public DateTime Date { get; set; }
    public int TotalLines { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public int NetLines { get; set; }
    public Dictionary<string, int> LinesByFileType { get; set; } = new();
    public int CommitCount { get; set; }
}
