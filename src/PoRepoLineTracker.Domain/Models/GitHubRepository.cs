namespace PoRepoLineTracker.Domain.Models;

public class GitHubRepository
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user who owns this repository tracking entry.
    /// Used to partition repositories by authenticated user.
    /// </summary>
    public Guid UserId { get; set; }
    
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public DateTime? LastAnalyzedCommitDate { get; set; } // Nullable to avoid Azure Table Storage DateTime.MinValue issue
    public string LocalPath { get; set; } = string.Empty;
}
