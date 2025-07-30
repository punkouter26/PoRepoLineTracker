using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;

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
    Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days); // Added for line count history
    Task<bool> CommitExistsAsync(Guid repositoryId, string commitSha);
    Task DeleteCommitLineCountsForRepositoryAsync(Guid repositoryId); // Added for temporary endpoint
    Task DeleteRepositoryAsync(Guid repositoryId);
    Task RemoveAllRepositoriesAsync(); // Added for removing all repositories and data
    Task CheckConnectionAsync();
    Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync(); // Added for file extensions
    Task AnalyzeRepositoryCommitsAsync(Guid repositoryId); // Added for commit analysis
}
