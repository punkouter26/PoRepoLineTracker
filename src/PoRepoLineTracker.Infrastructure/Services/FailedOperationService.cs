using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Infrastructure.Entities;

namespace PoRepoLineTracker.Infrastructure.Services;

/// <summary>
/// Strategy Pattern: Implementation of failed operation service using Azure Table Storage.
/// Provides dead letter queue functionality with retry mechanisms and exponential backoff.
/// </summary>
public class FailedOperationService : IFailedOperationService
{
    private readonly TableClient _failedOperationTableClient;
    private readonly ILogger<FailedOperationService> _logger;
    private bool _tablesInitialized = false;

    public FailedOperationService(IConfiguration configuration, ILogger<FailedOperationService> logger)
    {
        _logger = logger;
        var connectionString = configuration["AzureTableStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        var failedOperationTableName = configuration["AzureTableStorage:FailedOperationTableName"] ?? "PoRepoLineTrackerFailedOperations";

        _failedOperationTableClient = new TableClient(connectionString, failedOperationTableName);
    }

    private async Task EnsureTablesExistAsync()
    {
        if (!_tablesInitialized)
        {
            try
            {
                await _failedOperationTableClient.CreateIfNotExistsAsync();
                _tablesInitialized = true;
                _logger.LogInformation("Failed operations table created/verified successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring failed operations table exists: {ErrorMessage}", ex.Message);
                throw;
            }
        }
    }

    public async Task RecordFailedOperationAsync(FailedOperation failedOperation)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Recording failed operation for repository {RepositoryId}, operation {OperationType}, entity {EntityId}", 
            failedOperation.RepositoryId, failedOperation.OperationType, failedOperation.EntityId);

        try
        {
            var entity = FailedOperationEntity.FromDomainModel(failedOperation);
            await _failedOperationTableClient.AddEntityAsync(entity);
            _logger.LogInformation("Failed operation recorded successfully for repository {RepositoryId}", failedOperation.RepositoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording failed operation for repository {RepositoryId}: {ErrorMessage}", 
                failedOperation.RepositoryId, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<FailedOperation>> GetFailedOperationsByRepositoryIdAsync(Guid repositoryId)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting failed operations for repository {RepositoryId} from Table Storage.", repositoryId);

        var failedOperations = new List<FailedOperation>();
        try
        {
            await foreach (var entity in _failedOperationTableClient.QueryAsync<FailedOperationEntity>(e => e.PartitionKey == repositoryId.ToString()))
            {
                failedOperations.Add(entity.ToDomainModel());
            }
            _logger.LogInformation("Found {Count} failed operations for repository {RepositoryId}.", failedOperations.Count, repositoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting failed operations for repository {RepositoryId}: {ErrorMessage}", repositoryId, ex.Message);
            throw;
        }

        return failedOperations;
    }

    public async Task<FailedOperation?> GetFailedOperationByIdAsync(Guid id)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting failed operation by Id: {FailedOperationId} from Table Storage.", id);

        try
        {
            // Query by ID - this requires scanning but is acceptable for this use case
            await foreach (var entity in _failedOperationTableClient.QueryAsync<FailedOperationEntity>(e => e.Id == id))
            {
                _logger.LogInformation("Found failed operation {FailedOperationId}.", id);
                return entity.ToDomainModel();
            }
            _logger.LogWarning("Failed operation with Id {FailedOperationId} not found.", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting failed operation by Id {FailedOperationId}: {ErrorMessage}", id, ex.Message);
            throw;
        }
    }

public async Task UpdateFailedOperationAsync(FailedOperation failedOperation)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Updating failed operation {FailedOperationId} in Table Storage.", failedOperation.Id);

        try
        {
            var entity = FailedOperationEntity.FromDomainModel(failedOperation);
            // Use ETag.All to allow updates without checking the original ETag
            await _failedOperationTableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace);
            _logger.LogInformation("Failed operation {FailedOperationId} updated successfully.", failedOperation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating failed operation {FailedOperationId}: {ErrorMessage}", failedOperation.Id, ex.Message);
            throw;
        }
    }

    public async Task DeleteFailedOperationAsync(Guid id)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Deleting failed operation {FailedOperationId} from Table Storage.", id);

        try
        {
            // First find the entity to get its partition key and row key
            await foreach (var entity in _failedOperationTableClient.QueryAsync<FailedOperationEntity>(e => e.Id == id))
            {
                await _failedOperationTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag);
                _logger.LogInformation("Failed operation {FailedOperationId} deleted successfully.", id);
                return;
            }
            _logger.LogWarning("Failed operation {FailedOperationId} not found for deletion.", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting failed operation {FailedOperationId}: {ErrorMessage}", id, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<FailedOperation>> GetRetryableOperationsAsync(int maxRetryCount = 3)
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Getting retryable failed operations (max retry count: {MaxRetryCount}) from Table Storage.", maxRetryCount);

        var retryableOperations = new List<FailedOperation>();
        try
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-5); // Wait at least 5 minutes between retries
            
            await foreach (var entity in _failedOperationTableClient.QueryAsync<FailedOperationEntity>(
                e => e.RetryCount < maxRetryCount && 
                     (e.LastRetryAttempt == null || e.LastRetryAttempt < cutoffTime)))
            {
                retryableOperations.Add(entity.ToDomainModel());
            }
            _logger.LogInformation("Found {Count} retryable failed operations.", retryableOperations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting retryable failed operations: {ErrorMessage}", ex.Message);
            throw;
        }

        return retryableOperations;
    }

    public async Task CheckConnectionAsync()
    {
        await EnsureTablesExistAsync();
        _logger.LogInformation("Checking Azure Table Storage connection for failed operations.");
        
        try
        {
            // Attempt a simple query to check connectivity
            await foreach (var _ in _failedOperationTableClient.QueryAsync<TableEntity>(maxPerPage: 1))
            {
                // Just iterate once to ensure connection is established
                break;
            }
            _logger.LogInformation("Azure Table Storage connection for failed operations successful.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Azure Table Storage connection for failed operations: {ErrorMessage}", ex.Message);
            throw;
        }
    }
}
