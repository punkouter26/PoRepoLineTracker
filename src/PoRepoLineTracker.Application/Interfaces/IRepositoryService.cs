using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// Provides repository management operations including CRUD, analysis, and statistics.
/// Acts as the main application service for repository-related business logic.
/// </summary>
public interface IRepositoryService
{
    /// <summary>
    /// Gets all tracked repositories.
    /// </summary>
    /// <returns>A collection of all GitHub repositories.</returns>
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync();

    /// <summary>
    /// Gets a repository by its owner and name with statistics.
    /// </summary>
    /// <param name="owner">The repository owner (e.g., "microsoft").</param>
    /// <param name="repoName">The repository name (e.g., "vscode").</param>
    /// <returns>The repository statistics DTO, or null if not found.</returns>
    Task<GitHubRepoStatsDto?> GetRepositoryByOwnerAndNameAsync(string owner, string repoName);

    /// <summary>
    /// Adds a new repository to be tracked.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repoName">The repository name.</param>
    /// <param name="cloneUrl">The HTTPS clone URL.</param>
    /// <returns>The newly created repository entity.</returns>
    Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl);

    /// <summary>
    /// Adds multiple repositories in a single batch operation.
    /// </summary>
    /// <param name="repositories">The collection of repositories to add.</param>
    /// <returns>The newly created repository entities.</returns>
    Task<IEnumerable<GitHubRepository>> AddMultipleRepositoriesAsync(IEnumerable<BulkRepositoryDto> repositories);

    /// <summary>
    /// Triggers commit analysis for a specific repository.
    /// </summary>
    /// <param name="repositoryId">The unique identifier of the repository.</param>
    Task AnalyzeRepositoryCommitsAsync(Guid repositoryId);

    /// <summary>
    /// Updates an existing repository entity.
    /// </summary>
    /// <param name="repository">The repository entity with updated values.</param>
    Task UpdateRepositoryAsync(GitHubRepository repository);

    /// <summary>
    /// Gets all line counts recorded for a repository.
    /// </summary>
    /// <param name="repositoryId">The unique identifier of the repository.</param>
    /// <returns>A collection of commit line count records.</returns>
    Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId);

    /// <summary>
    /// Gets the daily line count history for a repository.
    /// </summary>
    /// <param name="repositoryId">The unique identifier of the repository.</param>
    /// <param name="days">The number of days of history to retrieve.</param>
    /// <returns>A collection of daily line count DTOs.</returns>
    Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days);

    /// <summary>
    /// Gets line count history for all repositories.
    /// </summary>
    /// <param name="days">The number of days of history to retrieve.</param>
    /// <returns>A collection of repository line count history DTOs.</returns>
    Task<IEnumerable<RepositoryLineCountHistoryDto>> GetAllRepositoriesLineCountHistoryAsync(int days);

    /// <summary>
    /// Gets the configured file extensions for line counting.
    /// </summary>
    /// <returns>A collection of file extensions (e.g., ".cs", ".js").</returns>
    Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync();

    /// <summary>
    /// Deletes a repository and all associated data.
    /// </summary>
    /// <param name="repositoryId">The unique identifier of the repository to delete.</param>
    Task DeleteRepositoryAsync(Guid repositoryId);

    /// <summary>
    /// Removes all repositories and their associated data.
    /// </summary>
    Task RemoveAllRepositoriesAsync();

    /// <summary>
    /// Gets the percentage breakdown of lines by file extension for a repository.
    /// </summary>
    /// <param name="repositoryId">The unique identifier of the repository.</param>
    /// <returns>A collection of file extension percentage DTOs.</returns>
    Task<IEnumerable<FileExtensionPercentageDto>> GetFileExtensionPercentagesAsync(Guid repositoryId);
}
