namespace PoRepoLineTracker.Client.Models;

/// <summary>
/// Client-side model for GitHub user repository from API
/// </summary>
public class GitHubUserRepositoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Private { get; set; }
    public string Language { get; set; } = string.Empty;
}
