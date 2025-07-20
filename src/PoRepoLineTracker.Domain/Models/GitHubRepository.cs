namespace PoRepoLineTracker.Domain.Models;

public class GitHubRepository
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public DateTime LastAnalyzedCommitDate { get; set; }
}
