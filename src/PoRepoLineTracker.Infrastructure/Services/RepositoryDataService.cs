using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Infrastructure.Entities;
using PoRepoLineTracker.Application.Interfaces;

namespace PoRepoLineTracker.Infrastructure.Services;

public class RepositoryDataService : IRepositoryDataService
{
    private readonly TableClient _repositoryTableClient;
    private readonly TableClient _commitLineCountTableClient;
    private readonly ILogger<RepositoryDataService> _logger;

    public RepositoryDataService(IConfiguration configuration, ILogger<RepositoryDataService> logger)
    {
        _logger = logger;
        var connectionString = configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var repositoryTableName = configuration["AzureTableStorage:RepositoryTableName"] ?? "PoRepoLineTrackerRepositories";
        var commitLineCountTableName = configuration["AzureTableStorage:CommitLineCountTableName"] ?? "PoRepoLineTrackerCommitLineCounts";

        _repositoryTableClient = new TableClient(connectionString, repositoryTableName);
        _commitLineCountTableClient = new TableClient(connectionString, commitLineCountTableName);

        _repositoryTableClient.CreateIfNotExists();
        _commitLineCountTableClient.CreateIfNotExists();
    }

    public async Task AddRepositoryAsync(GitHubRepository repository)
    {
        _logger.LogInformation("Adding repository {RepoName} to Table Storage.", repository.Name);
        var entity = GitHubRepositoryEntity.FromDomainModel(repository);
        await _repositoryTableClient.AddEntityAsync(entity);
        _logger.LogInformation("Repository {RepoName} added successfully.", repository.Name);
    }

    public async Task UpdateRepositoryAsync(GitHubRepository repository)
    {
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
        _logger.LogInformation("Upserting commit line count for commit {CommitSha} of repository {RepositoryId}.", commitLineCount.CommitSha, commitLineCount.RepositoryId);
        var entity = CommitLineCountEntity.FromDomainModel(commitLineCount);
        // Use UpsertEntityAsync to add or replace the entity
        await _commitLineCountTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        _logger.LogInformation("Commit line count for commit {CommitSha} upserted successfully.", commitLineCount.CommitSha);
    }

    public async Task<IEnumerable<CommitLineCount>> GetCommitLineCountsByRepositoryIdAsync(Guid repositoryId)
    {
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
}
