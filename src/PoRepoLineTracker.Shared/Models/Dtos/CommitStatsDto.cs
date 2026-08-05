namespace PoRepoLineTracker.Shared.Models.Dtos;

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


    /// <summary>
    /// Heuristic AI-authorship score (0–100) for the lines this commit ADDED.
    /// <para>
    /// Scored from the commit's own diff rather than the whole working tree: the question is how
    /// this commit was written, and the tree is mostly code the commit never touched. It is also
    /// what makes the score affordable — the patch is already materialised to count
    /// added/removed lines, so scoring it costs one extra pass over text already in hand.
    /// </para>
    /// </summary>
    public double AiPercentage { get; set; }
}
