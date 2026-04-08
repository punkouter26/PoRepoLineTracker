using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Models;

/// <summary>
/// Returned by the bulk-add endpoint so the caller can show distinct banners
/// for newly added vs. already-tracked repositories.
/// </summary>
public class BulkAddResult
{
    /// <summary>Repositories that were created during this call.</summary>
    public List<GitHubRepository> Added { get; init; } = [];

    /// <summary>Repositories that already existed and were not re-created.</summary>
    public List<GitHubRepository> AlreadyTracked { get; init; } = [];
}
