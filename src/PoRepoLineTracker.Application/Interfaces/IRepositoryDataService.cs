using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IRepositoryDataService
{
    Task AddRepositoryAsync(GitHubRepository repository);
    Task UpdateRepositoryAsync(GitHubRepository repository);
    Task<GitHubRepository?> GetRepositoryByIdAsync(Guid id);
    Task<GitHubRepository?> GetRepositoryByOwnerAndNameAsync(string owner, string name, Guid userId);
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync(Guid userId);

    Task AddCommitLineCountAsync(CommitLineCount commitLineCount);
    Task<IEnumerable<CommitLineCount>> GetCommitLineCountsByRepositoryIdAsync(Guid repositoryId);
    Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days); // Added for line count history
    Task<bool> CommitExistsAsync(Guid repositoryId, string commitSha);
    Task DeleteCommitLineCountsForRepositoryAsync(Guid repositoryId); // Added for temporary endpoint
    Task DeleteRepositoryAsync(Guid repositoryId);
    Task RemoveAllRepositoriesAsync(Guid userId); // Removes all repositories for a specific user
    Task CheckConnectionAsync();
    Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync(); // Added for file extensions
    Task AnalyzeRepositoryCommitsAsync(Guid repositoryId); // Added for commit analysis
    
    // Top files storage (calculated during analysis, stored for retrieval without local git clone)
    Task SaveTopFilesAsync(Guid repositoryId, IEnumerable<TopFileDto> topFiles);
    Task<IEnumerable<TopFileDto>> GetTopFilesAsync(Guid repositoryId, int count = 5);
    Task DeleteTopFilesForRepositoryAsync(Guid repositoryId);
}
