namespace PoRepoLineTracker.Shared.Domain;

public class CommitLineCount
{
    public Guid Id { get; set; }
    public RepositoryId RepositoryId { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
    public int TotalLines { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public Dictionary<string, int> LinesByFileType { get; set; } = new(); // e.g., ".cs": 1000, ".js": 500

    /// <summary>
    /// Author name from the commit.
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Author email from the commit.
    /// </summary>
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>
    /// AI detection percentage (0-100) for this commit.
    /// </summary>
    public double AiPercentage { get; set; }
}
