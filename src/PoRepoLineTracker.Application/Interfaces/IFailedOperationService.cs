using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// Strategy Pattern: Interface for managing failed operations with retry capabilities.
/// Provides dead letter queue functionality for failed commit processing and other operations.
/// </summary>
public interface IFailedOperationService
{
    /// <summary>
    /// Records a failed operation for later analysis or retry
    /// </summary>
    Task RecordFailedOperationAsync(FailedOperation failedOperation);

    /// <summary>
    /// Gets all failed operations for a specific repository
    /// </summary>
    Task<IEnumerable<FailedOperation>> GetFailedOperationsByRepositoryIdAsync(Guid repositoryId);

    /// <summary>
    /// Gets a specific failed operation by ID
    /// </summary>
    Task<FailedOperation?> GetFailedOperationByIdAsync(Guid id);

    /// <summary>
    /// Updates a failed operation (e.g., incrementing retry count)
    /// </summary>
    Task UpdateFailedOperationAsync(FailedOperation failedOperation);

    /// <summary>
    /// Deletes a failed operation (e.g., after successful retry)
    /// </summary>
    Task DeleteFailedOperationAsync(Guid id);

    /// <summary>
    /// Gets all failed operations that are ready for retry
    /// </summary>
    Task<IEnumerable<FailedOperation>> GetRetryableOperationsAsync(int maxRetryCount = 3);

    /// <summary>
    /// Checks connection to the failed operations storage
    /// </summary>
    Task CheckConnectionAsync();
}
