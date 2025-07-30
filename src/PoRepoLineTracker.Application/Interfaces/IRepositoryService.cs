using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IRepositoryService
{
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync();
    Task<GitHubRepoStatsDto?> GetRepositoryByOwnerAndNameAsync(string owner, string repoName);
    Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl);
    Task<IEnumerable<GitHubRepository>> AddMultipleRepositoriesAsync(IEnumerable<BulkRepositoryDto> repositories);
    Task AnalyzeRepositoryCommitsAsync(Guid repositoryId);
    Task UpdateRepositoryAsync(GitHubRepository repository);
    Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId);
    Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days);
    Task<IEnumerable<RepositoryLineCountHistoryDto>> GetAllRepositoriesLineCountHistoryAsync(int days);
    Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync();
    Task DeleteRepositoryAsync(Guid repositoryId);
    Task RemoveAllRepositoriesAsync(); // Added for removing all repositories and data
    Task<IEnumerable<FileExtensionPercentageDto>> GetFileExtensionPercentagesAsync(Guid repositoryId);
}
