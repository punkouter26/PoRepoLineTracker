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

    private const string RepositoryTableName = "PoRepoLineTrackerRepositories";
    private const string CommitLineCountTableName = "PoRepoLineTrackerCommitLineCounts";

    public RepositoryDataService(IConfiguration configuration, ILogger<RepositoryDataService> logger)
    {
        _logger = logger;
        var connectionString = configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";

        _repositoryTableClient = new TableClient(connectionString, RepositoryTableName);
        _commitLineCountTableClient = new TableClient(connectionString, CommitLineCountTableName);

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
        var existingEntity = await _repositoryTableClient.GetEntityAsync<GitHubRepositoryEntity>(repository.Id.ToString(), repository.Id.ToString());
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
        _logger.LogInformation("Adding commit line count for commit {CommitSha} of repository {RepositoryId}.", commitLineCount.CommitSha, commitLineCount.RepositoryId);
        var entity = CommitLineCountEntity.FromDomainModel(commitLineCount);
        await _commitLineCountTableClient.AddEntityAsync(entity);
        _logger.LogInformation("Commit line count for commit {CommitSha} added successfully.", commitLineCount.CommitSha);
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
}
