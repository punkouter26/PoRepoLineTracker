namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Unified DTO for a GitHub repository returned to the client.
/// Shared between API responses and Blazor WASM client deserialization.
/// </summary>
public class GitHubUserRepositoryDto
{
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public string Language { get; set; } = string.Empty;
}
