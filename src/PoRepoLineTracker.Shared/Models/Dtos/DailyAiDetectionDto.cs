namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Represents daily AI detection statistics across all commits for a repository.
/// Shared DTO: used by API responses and deserialized by the Blazor WASM client.
/// </summary>
public class DailyAiDetectionDto
{
    public DateTime Date { get; set; }
    public int CommitCount { get; set; }
    public double AverageAiPercentage { get; set; }
    public Dictionary<string, double> AuthorBreakdown { get; set; } = new();
}
