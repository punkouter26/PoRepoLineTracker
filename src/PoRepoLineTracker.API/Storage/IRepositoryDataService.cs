
namespace PoRepoLineTracker.API.Storage;

public interface IRepositoryDataService
{
    Task AddRepositoryAsync(GitHubRepository repository);
    Task UpdateRepositoryAsync(GitHubRepository repository);
    Task<GitHubRepository?> GetRepositoryByIdAsync(RepositoryId id);
    Task<GitHubRepository?> GetRepositoryByOwnerAndNameAsync(string owner, string name, UserId userId);
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync(UserId userId);

    Task AddCommitLineCountAsync(CommitLineCount commitLineCount);
    Task<IEnumerable<CommitLineCount>> GetCommitLineCountsByRepositoryIdAsync(RepositoryId repositoryId);
    Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(RepositoryId repositoryId, int days); // Added for line count history

    // No CommitExistsAsync. A per-SHA existence check invites being called from inside a loop over
    // commits, which is exactly what happened: N round-trips to answer what the one call above
    // returns for the whole repository. Callers pre-load and look up in memory.

    Task DeleteCommitLineCountsForRepositoryAsync(RepositoryId repositoryId); // Added for temporary endpoint
    Task DeleteRepositoryAsync(RepositoryId repositoryId);
    Task RemoveAllRepositoriesAsync(UserId userId); // Removes all repositories for a specific user
    Task CheckConnectionAsync();
}
