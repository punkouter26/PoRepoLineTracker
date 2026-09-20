using PoRepoLineTracker.Shared.Domain;

namespace PoRepoLineTracker.Client.Models;

/// <summary>
/// Row shape for the <c>RepositoriesGrid</c> Radzen column templates. The four column-bound
/// properties (<see cref="Owner"/>, <see cref="Name"/>, <see cref="LastAnalyzedCommitDate"/>,
/// <see cref="TotalLinesSortValue"/>) must keep their names — the column templates and the
/// sort handler reference them by symbol, so a rename is a wire-shape change at the grid level.
/// </summary>
public sealed record RepositoryGridRow(
    GitHubRepository Repository,
    int? TotalLinesSortValue)
{
    public RepositoryId Id => Repository.Id;
    public string Owner => Repository.Owner;
    public string Name => Repository.Name;
    public DateTime? LastAnalyzedCommitDate => Repository.LastAnalyzedCommitDate;
}
