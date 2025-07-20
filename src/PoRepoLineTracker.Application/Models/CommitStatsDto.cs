namespace PoRepoLineTracker.Application.Models;

public class CommitStatsDto
{
    public string Sha { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
    public int TotalLines { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public Dictionary<string, int> LinesByFileType { get; set; } = new();
    public string CommitMessage { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
}
