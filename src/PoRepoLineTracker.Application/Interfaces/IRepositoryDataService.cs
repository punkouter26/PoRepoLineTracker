using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IRepositoryDataService
{
    Task AddRepositoryAsync(GitHubRepository repository);
    Task UpdateRepositoryAsync(GitHubRepository repository);
    Task<GitHubRepository?> GetRepositoryByIdAsync(Guid id);
    Task<GitHubRepository?> GetRepositoryByOwnerAndNameAsync(string owner, string name);
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync();

    Task AddCommitLineCountAsync(CommitLineCount commitLineCount);
    Task<IEnumerable<CommitLineCount>> GetCommitLineCountsByRepositoryIdAsync(Guid repositoryId);
    Task<bool> CommitExistsAsync(Guid repositoryId, string commitSha);
    Task DeleteCommitLineCountsForRepositoryAsync(Guid repositoryId); // Added for temporary endpoint
    Task DeleteRepositoryAsync(Guid repositoryId);
    Task CheckConnectionAsync();
}
