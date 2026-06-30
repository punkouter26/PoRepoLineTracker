namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Returned by the bulk-add endpoint so the caller can show distinct banners
/// for newly added vs. already-tracked repositories.
/// </summary>
public class BulkAddResult
{
    public List<GitHubRepositoryDto> Added { get; init; } = [];
    public List<GitHubRepositoryDto> AlreadyTracked { get; init; } = [];
}
