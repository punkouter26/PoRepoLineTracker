namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Represents AI detection statistics grouped by user/author.
/// Shared DTO: used by API responses and deserialized by the Blazor WASM client.
/// </summary>
public class AiStatsByUserDto
{
    public string AuthorName { get; set; } = string.Empty;
    public double AverageAiPercentage { get; set; }
    public int TotalCommits { get; set; }
    public List<UserAiPercentagePerCommitDto> CommitHistory { get; set; } = new();
}

/// <summary>
/// Represents AI detection percentage for a single commit by a user.
/// </summary>
public class UserAiPercentagePerCommitDto
{
    public DateTime Date { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public double AiPercentage { get; set; }
    public int LinesAdded { get; set; }
}
