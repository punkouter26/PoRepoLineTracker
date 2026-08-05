using PoRepoLineTracker.Domain.Models;
namespace PoRepoLineTracker.Shared.Models.Dtos;

public class RepositoryLineCountHistoryDto
{
    public RepositoryId RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    /// The repository's current size — the snapshot on its newest commit, whether or not that
    /// commit falls inside the requested window. See <see cref="RepositoryTotals"/> for why this
    /// is carried explicitly rather than read off the last point of <see cref="DailyLineCounts"/>:
    /// a repository untouched for longer than the window has an empty series and still has code.
    /// </summary>
    public int TotalLines { get; set; }

    /// <summary>Daily snapshots WITHIN the requested window. Empty for a repository with no commits in it.</summary>
    public IEnumerable<DailyLineCountDto> DailyLineCounts { get; set; } = new List<DailyLineCountDto>();
}
