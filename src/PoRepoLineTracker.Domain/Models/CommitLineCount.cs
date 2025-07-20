namespace PoRepoLineTracker.Domain.Models;

public class CommitLineCount
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
    public int TotalLines { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public Dictionary<string, int> LinesByFileType { get; set; } = new(); // e.g., ".cs": 1000, ".js": 500
}
