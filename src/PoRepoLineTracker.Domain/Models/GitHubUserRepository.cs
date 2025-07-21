namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// Represents a GitHub repository returned from the GitHub API for the authenticated user
/// </summary>
public class GitHubUserRepository
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public string Language { get; set; } = string.Empty;
}
