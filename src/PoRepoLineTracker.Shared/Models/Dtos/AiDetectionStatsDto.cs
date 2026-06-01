namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Represents AI detection statistics for a specific user across all their commits.
/// Shared DTO: used by API responses and deserialized by the Blazor WASM client.
/// </summary>
public class AiDetectionStatsDto
{
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public int TotalCommits { get; set; }
    public int AiDetectedCommits { get; set; }
    public double AiPercentage { get; set; }
}
