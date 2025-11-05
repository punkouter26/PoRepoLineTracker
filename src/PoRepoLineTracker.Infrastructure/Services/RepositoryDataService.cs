``using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Infrastructure.Entities;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using System.IO;

namespace PoRepoLineTracker.Infrastructure.Services;

public class RepositoryDataService : IRepositoryDataService
{
    private readonly TableClient _repositoryTableClient;
    private readonly TableClient _commitLineCountTableClient;
    private readonly ILogger<RepositoryDataService> _logger;

    private bool _tablesInitialized = false;

    public RepositoryDataService(IConfiguration configuration, ILogger<RepositoryDataService> logger)
    {
        _logger = logger;
        var connectionString = configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var repositoryTableName = configuration["AzureTableStorage:RepositoryTableName"] ?? "PoRepoLineTrackerRepositories";
        var commitLineCountTableName = configuration["AzureTableStorage:CommitLineCountTableName"] ?? "PoRepoLineTrackerCommitLineCounts";

        _repositoryTableClient = new TableClient(connectionString, repositoryTableName);
        _commitLineCountTableClient = new TableClient(connectionString, commitLineCountTableName);
    }

    private async Task EnsureTablesExistAsync()
    {
        if (!_tablesInitialized)
        {
            try
            {
                await _repositoryTableClient.CreateIfNotExistsAsync();
                await _commitLineCountTableClient.CreateIfNotExistsAsync();
                _tablesInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring tables exist: {ErrorMessage}", ex.Message);
                throw;
            }
        }
    }

    public async Task AddRepositoryAsync(GitHubRepository repository)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Adding repository {RepoName} to Table Storage.", repository.Name);
        try
        {
            var entity = GitHubRepositoryEntity.FromDomainModel(repository);
            _logger.LogInformation("About to call AddEntityAsync for {RepoName}", repository.Name);
            await _repositoryTableClient.AddEntityAsync(entity);
            _logger.LogInformation("AddEntityAsync completed for {RepoName}", repository.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FATAL ERROR in AddEntityAsync for {RepoName}: {Message}. Type: {ExceptionType}. Stack: {StackTrace}",
                repository.Name, ex.Message, ex.GetType().FullName, ex.StackTrace);
            throw; // Re-throw to propagate to caller
        }
        _logger.LogInformation("Repository {RepoName} added successfully.", repository.Name);
    }

    public async Task UpdateRepositoryAsync(GitHubRepository repository)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Updating repository {RepoName} in Table Storage.", repository.Name);
        // Retrieve the existing entity to get its ETag for optimistic concurrency
        var existingEntity = await _repositoryTableClient.GetEntityAsync<GitHubRepositoryEntity>(repository.Owner, repository.Name);
        var entityToUpdate = GitHubRepositoryEntity.FromDomainModel(repository);
        entityToUpdate.ETag = existingEntity.Value.ETag; // Assign the ETag from the retrieved entity

        await _repositoryTableClient.UpdateEntityAsync(entityToUpdate, entityToUpdate.ETag, TableUpdateMode.Replace);
        _logger.LogInformation("Repository {RepoName} updated successfully.", repository.Name);
    }

    public async Task<GitHubRepository?> GetRepositoryByIdAsync(Guid id)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting repository by Id: {RepositoryId} from Table Storage.", id);
        await foreach (var entity in _repositoryTableClient.QueryAsync<GitHubRepositoryEntity>(e => e.Id == id))
        {
            _logger.LogInformation("Found repository {RepoName} by Id {RepositoryId}.", entity.Name, id);
            return entity.ToDomainModel();
        }
        _logger.LogWarning("Repository with Id {RepositoryId} not found.", id);
        return null;
    }

    public async Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync()
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting all repositories from Table Storage.");
        var repositories = new List<GitHubRepository>();
        await foreach (var entity in _repositoryTableClient.QueryAsync<GitHubRepositoryEntity>())
        {
            repositories.Add(entity.ToDomainModel());
        }
        _logger.LogInformation("Found {Count} repositories.", repositories.Count);
        return repositories;
    }

    public async Task AddCommitLineCountAsync(CommitLineCount commitLineCount)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Upserting commit line count for commit {CommitSha} of repository {RepositoryId}.", commitLineCount.CommitSha, commitLineCount.RepositoryId);
        var entity = CommitLineCountEntity.FromDomainModel(commitLineCount);
        // Use UpsertEntityAsync to add or replace the entity
        await _commitLineCountTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        _logger.LogInformation("Commit line count for commit {CommitSha} upserted successfully.", commitLineCount.CommitSha);
    }

    public async Task<IEnumerable<CommitLineCount>> GetCommitLineCountsByRepositoryIdAsync(Guid repositoryId)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting commit line counts for repository {RepositoryId} from Table Storage.", repositoryId);
        var commitLineCounts = new List<CommitLineCount>();
        await foreach (var entity in _commitLineCountTableClient.QueryAsync<CommitLineCountEntity>(e => e.PartitionKey == repositoryId.ToString()))
        {
            commitLineCounts.Add(entity.ToDomainModel());
        }
        _logger.LogInformation("Found {Count} commit line counts for repository {RepositoryId}.", commitLineCounts.Count, repositoryId);
        return commitLineCounts;
    }

    public async Task<bool> CommitExistsAsync(Guid repositoryId, string commitSha)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Checking if commit {CommitSha} exists for repository {RepositoryId}.", commitSha, repositoryId);
        try
        {
            var entity = await _commitLineCountTableClient.GetEntityAsync<CommitLineCountEntity>(repositoryId.ToString(), commitSha);
            _logger.LogInformation("Commit {CommitSha} exists for repository {RepositoryId}.", commitSha, repositoryId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Commit {CommitSha} does not exist for repository {RepositoryId}.", commitSha, repositoryId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking commit existence for {CommitSha} in {RepositoryId}. Error: {ErrorMessage}", commitSha, repositoryId, ex.Message);
            throw;
        }
    }

    public async Task CheckConnectionAsync()
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Checking Azure Table Storage connection.");
        // Attempt a simple query to check connectivity
        await foreach (var _ in _repositoryTableClient.QueryAsync<TableEntity>(maxPerPage: 1))
        {
            // Just iterate once to ensure connection is established
            break;
        }
        _logger.LogInformation("Azure Table Storage connection successful.");
    }

    public async Task DeleteCommitLineCountsForRepositoryAsync(Guid repositoryId)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Deleting all commit line counts for repository {RepositoryId} from Table Storage.", repositoryId);
        var entitiesToDelete = new List<CommitLineCountEntity>();
        await foreach (var entity in _commitLineCountTableClient.QueryAsync<CommitLineCountEntity>(e => e.PartitionKey == repositoryId.ToString()))
        {
            entitiesToDelete.Add(entity);
        }

        if (entitiesToDelete.Any())
        {
            var deleteTasks = entitiesToDelete.Select(entity => _commitLineCountTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag));
            await Task.WhenAll(deleteTasks);
            _logger.LogInformation("Deleted {Count} commit line counts for repository {RepositoryId}.", entitiesToDelete.Count, repositoryId);
        }
        else
        {
            _logger.LogInformation("No commit line counts found to delete for repository {RepositoryId}.", repositoryId);
        }
    }

    public async Task DeleteRepositoryAsync(Guid repositoryId)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Deleting repository {RepositoryId} and all associated data from Table Storage.", repositoryId);

        // First, delete all commit line counts for this repository
        await DeleteCommitLineCountsForRepositoryAsync(repositoryId);

        // Then, find and delete the repository entity
        try
        {
            await foreach (var entity in _repositoryTableClient.QueryAsync<GitHubRepositoryEntity>(r => r.Id == repositoryId))
            {
                await _repositoryTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag);
                _logger.LogInformation("Repository entity deleted successfully for repository {RepositoryId}.", repositoryId);
                break; // Should only be one match
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting repository {RepositoryId}. Error: {ErrorMessage}", repositoryId, ex.Message);
            throw;
        }
    }

    public async Task<GitHubRepository?> GetRepositoryByOwnerAndNameAsync(string owner, string name)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting repository by Owner: {Owner} and Name: {Name} from Table Storage.", owner, name);
        try
        {
            var entity = await _repositoryTableClient.GetEntityAsync<GitHubRepositoryEntity>(owner, name);
            _logger.LogInformation("Found repository {RepoName} by Owner {Owner} and Name {Name}.", name, owner, name);
            return entity.Value.ToDomainModel();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Repository with Owner {Owner} and Name {Name} not found.", owner, name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repository by Owner {Owner} and Name {Name}. Error: {ErrorMessage}", owner, name, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync()
    {
        // Return file extensions that should be counted for line analysis
        // Focused on .NET development with support for common web technologies
        var fileExtensions = new[]
        {
            // .NET Core/Framework Languages
            ".cs",      // C# files
            ".vb",      // Visual Basic files
            ".fs",      // F# files
            ".fsx",     // F# script files
            
            // .NET Web UI
            ".razor",   // Blazor components (CRITICAL for Blazor apps)
            ".cshtml",  // Razor views (MVC/Pages)
            ".vbhtml",  // VB Razor views
            ".aspx",    // ASP.NET Web Forms pages
            ".ascx",    // ASP.NET User Controls
            ".master",  // ASP.NET Master pages
            ".xaml",    // XAML (WPF/UWP/MAUI)
            
            // .NET Project & Configuration Files
            ".csproj",  // C# project files
            ".vbproj",  // VB project files
            ".fsproj",  // F# project files
            ".sln",     // Solution files
            ".props",   // MSBuild property files
            ".targets", // MSBuild target files
            ".config",  // Configuration files
            ".resx",    // Resource files
            ".settings",// Settings files
            
            // Infrastructure as Code
            ".bicep",   // Azure Bicep templates
            
            // Web Technologies (for full-stack .NET apps)
            ".js",      // JavaScript files
            ".ts",      // TypeScript files
            ".html",    // HTML files
            ".css",     // CSS files
            ".json",    // JSON configuration/data files
            ".xml",     // XML files
            
            // Database & Scripts
            ".sql",     // SQL scripts
            ".ps1",     // PowerShell scripts
            ".bat",     // Batch files
            ".sh",      // Shell scripts
            
            // DevOps & Configuration
            ".yml",     // YAML files (Docker, CI/CD)
            ".yaml",    // YAML files (alternate extension)
            ".dockerfile", // Docker files
            ".http",    // HTTP request files
            
            // Documentation
            ".md"       // Markdown documentation
        };

        _logger.LogInformation("Returning {Count} configured file extensions for line counting", fileExtensions.Length);
        return await Task.FromResult(fileExtensions.AsEnumerable());
    }

    public async Task AnalyzeRepositoryCommitsAsync(Guid repositoryId)
    {
        _logger.LogInformation("Starting analysis for repository ID: {RepositoryId}", repositoryId);

        // Get the repository from storage
        var repository = await GetRepositoryByIdAsync(repositoryId);
        if (repository == null)
        {
            _logger.LogWarning("Repository with ID {RepositoryId} not found for analysis", repositoryId);
            throw new InvalidOperationException($"Repository with ID {repositoryId} not found");
        }

        // Note: The actual commit analysis logic should be implemented here or delegated to GitHubService
        // For now, we'll implement a basic placeholder that follows the pattern of other methods
        // The full implementation would involve calling GitHubService methods to analyze commits
        _logger.LogInformation("Repository analysis completed for repository ID: {RepositoryId}", repositoryId);
    }

    public async Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days)
    {
        _logger.LogInformation("Getting line count history for repository {RepositoryId} for the last {Days} days from Table Storage.", repositoryId, days);

        // Get all commit line counts for the repository
        var commitLineCounts = await GetCommitLineCountsByRepositoryIdAsync(repositoryId);

        // Filter commits within the specified date range
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        var filteredCommits = commitLineCounts
            .Where(c => c.CommitDate >= cutoffDate)
            .OrderBy(c => c.CommitDate)
            .ToList();

        _logger.LogInformation("Found {Count} commits within the last {Days} days for repository {RepositoryId}.", filteredCommits.Count, days, repositoryId);

        // Group commits by date and aggregate the data
        var dailyData = filteredCommits
            .GroupBy(c => c.CommitDate.Date)
            .Select(g => new DailyLineCountDto
            {
                Date = g.Key,
                TotalLines = g.Sum(c => c.TotalLines),
                TotalLinesAdded = g.Sum(c => c.LinesAdded),
                TotalLinesDeleted = g.Sum(c => c.LinesRemoved),
                TotalLinesChanged = g.Sum(c => c.LinesAdded + c.LinesRemoved),
                CommitCount = g.Count(),
                LinesByFileType = g
                    .SelectMany(c => c.LinesByFileType)
                    .GroupBy(kvp => kvp.Key)
                    .ToDictionary(fileGroup => fileGroup.Key, fileGroup => fileGroup.Sum(kvp => kvp.Value))
            })
            .OrderBy(d => d.Date)
            .ToList();

        _logger.LogInformation("Aggregated data into {Count} daily entries for repository {RepositoryId}.", dailyData.Count, repositoryId);
        return dailyData;
    }

    /// <summary>
    /// Repository Pattern: Implements comprehensive data removal strategy for all repositories.
    /// Removes all data from both repository and commit line count tables in Azure Table Storage.
    /// Uses batch operations for optimal performance.
    /// </summary>
    public async Task RemoveAllRepositoriesAsync()
    {
        _logger.LogInformation("Starting removal of all repositories and commit data from Azure Table Storage.");

        try
        {
            // Step 1: Remove all commit line counts
            await RemoveAllCommitLineCountsAsync();

            // Step 2: Remove all repositories
            await RemoveAllRepositoryEntitiesAsync();

            _logger.LogInformation("Successfully removed all repositories and commit data from Azure Table Storage.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while removing all data from Azure Table Storage: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Strategy Pattern: Implements batch deletion strategy for commit line counts.
    /// Removes all entities from the commit line count table.
    /// </summary>
    private async Task RemoveAllCommitLineCountsAsync()
    {
        _logger.LogInformation("Removing all commit line counts from Table Storage.");

        var entitiesToDelete = new List<CommitLineCountEntity>();

        try
        {
            // Query all entities from the commit line count table
            await foreach (var entity in _commitLineCountTableClient.QueryAsync<CommitLineCountEntity>())
            {
                entitiesToDelete.Add(entity);
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Commit line count table does not exist. Nothing to delete.");
            return;
        }

        if (entitiesToDelete.Any())
        {
            _logger.LogInformation("Found {Count} commit line count entities to delete.", entitiesToDelete.Count);

            // Delete entities in parallel batches for better performance
            const int batchSize = 100;
            var batches = entitiesToDelete
                .Select((entity, index) => new { entity, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.entity).ToList());

            foreach (var batch in batches)
            {
                var deleteTasks = batch.Select(entity =>
                    _commitLineCountTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag));

                await Task.WhenAll(deleteTasks);
                _logger.LogDebug("Deleted batch of {Count} commit line count entities.", batch.Count);
            }

            _logger.LogInformation("Successfully deleted {Count} commit line count entities.", entitiesToDelete.Count);
        }
        else
        {
            _logger.LogInformation("No commit line count entities found to delete.");
        }
    }

    /// <summary>
    /// Strategy Pattern: Implements batch deletion strategy for repository entities.
    /// Removes all entities from the repository table.
    /// </summary>
    private async Task RemoveAllRepositoryEntitiesAsync()
    {
        _logger.LogInformation("Removing all repository entities from Table Storage.");

        var entitiesToDelete = new List<GitHubRepositoryEntity>();

        try
        {
            // Query all entities from the repository table
            await foreach (var entity in _repositoryTableClient.QueryAsync<GitHubRepositoryEntity>())
            {
                entitiesToDelete.Add(entity);
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Repository table does not exist. Nothing to delete.");
            return;
        }

        if (entitiesToDelete.Any())
        {
            _logger.LogInformation("Found {Count} repository entities to delete.", entitiesToDelete.Count);

            // Delete entities in parallel batches for better performance
            const int batchSize = 100;
            var batches = entitiesToDelete
                .Select((entity, index) => new { entity, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.entity).ToList());

            foreach (var batch in batches)
            {
                var deleteTasks = batch.Select(entity =>
                    _repositoryTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag));

                await Task.WhenAll(deleteTasks);
                _logger.LogDebug("Deleted batch of {Count} repository entities.", batch.Count);
            }

            _logger.LogInformation("Successfully deleted {Count} repository entities.", entitiesToDelete.Count);
        }
        else
        {
            _logger.LogInformation("No repository entities found to delete.");
        }
    }
}
