using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Application.Interfaces;

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
    Task<bool> CommitExistsAsync(RepositoryId repositoryId, string commitSha);
    Task DeleteCommitLineCountsForRepositoryAsync(RepositoryId repositoryId); // Added for temporary endpoint
    Task DeleteRepositoryAsync(RepositoryId repositoryId);
    Task RemoveAllRepositoriesAsync(UserId userId); // Removes all repositories for a specific user
    Task CheckConnectionAsync();
    Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync(); // Added for file extensions
    Task AnalyzeRepositoryCommitsAsync(RepositoryId repositoryId); // Added for commit analysis

    // Top files storage (calculated during analysis, stored for retrieval without local git clone)
    Task SaveTopFilesAsync(RepositoryId repositoryId, IEnumerable<TopFileDto> topFiles);
    Task<IEnumerable<TopFileDto>> GetTopFilesAsync(RepositoryId repositoryId, int count = 5);
    Task DeleteTopFilesForRepositoryAsync(RepositoryId repositoryId);
}
