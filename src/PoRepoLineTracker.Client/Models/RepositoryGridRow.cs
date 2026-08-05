using PoRepoLineTracker.Shared.Domain;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Client.Models;

/// <summary>
/// One row of the repositories grid: the repository itself plus the two derived values the grid
/// shows beside it, both of which come from the <c>/allcharts/365</c> response the page already
/// fetches.
///
/// <para>This was a private nested record inside Repositories.razor. It moved out with the grid —
/// the page composes the rows, the grid renders them, and a type on the boundary between two
/// components cannot be private to either.</para>
/// </summary>
public sealed record RepositoryGridRow(
    GitHubRepository Repository,
    int? TotalLinesSortValue,
    IReadOnlyList<DailyLineCountDto> Trend)
{
    public RepositoryId Id => Repository.Id;
    public string Owner => Repository.Owner;
    public string Name => Repository.Name;
    public DateTime? LastAnalyzedCommitDate => Repository.LastAnalyzedCommitDate;

    /// <summary>A sparkline of one or two points is a dot, not a trend — hide it below three.</summary>
    public bool HasTrend => Trend.Count > 2;
}
