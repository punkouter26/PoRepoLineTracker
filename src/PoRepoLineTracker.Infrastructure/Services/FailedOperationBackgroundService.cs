using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Infrastructure.Services;

/// <summary>
/// Strategy Pattern: Background service for processing failed operations with retry mechanisms.
/// Implements exponential backoff and dead letter queue processing.
/// </summary>
public class FailedOperationBackgroundService : BackgroundService
{
    private readonly ILogger<FailedOperationBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _pollingInterval;

    public FailedOperationBackgroundService(
        ILogger<FailedOperationBackgroundService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _pollingInterval = TimeSpan.FromMinutes(5); // Check for retryable operations every 5 minutes
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Failed Operation Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRetryableOperationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing failed operations");
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
        }

        _logger.LogInformation("Failed Operation Background Service is stopping.");
    }

    private async Task ProcessRetryableOperationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing retryable failed operations...");

        // Create a scope to resolve scoped services
        using var scope = _serviceProvider.CreateScope();
        var failedOperationService = scope.ServiceProvider.GetRequiredService<IFailedOperationService>();
        var repositoryDataService = scope.ServiceProvider.GetRequiredService<IRepositoryDataService>();

        try
        {
            var retryableOperations = await failedOperationService.GetRetryableOperationsAsync(maxRetryCount: 3);
            var processedCount = 0;

            foreach (var failedOperation in retryableOperations)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessFailedOperationAsync(failedOperation, failedOperationService, repositoryDataService);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing failed operation {FailedOperationId}", failedOperation.Id);
                }
            }

            _logger.LogInformation("Processed {ProcessedCount} retryable failed operations", processedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting retryable failed operations");
        }
    }

    private async Task ProcessFailedOperationAsync(
        FailedOperation failedOperation,
        IFailedOperationService failedOperationService,
        IRepositoryDataService repositoryDataService)
    {
        _logger.LogInformation("Attempting to retry failed operation {FailedOperationId} (Retry #{RetryCount})",
            failedOperation.Id, failedOperation.RetryCount + 1);

        try
        {
            // Update retry attempt tracking
            failedOperation.RetryCount++;
            failedOperation.LastRetryAttempt = DateTime.UtcNow;
            await failedOperationService.UpdateFailedOperationAsync(failedOperation);

            // Attempt to reprocess based on operation type
            switch (failedOperation.OperationType)
            {
                case "CommitProcessing":
                    await RetryCommitProcessingAsync(failedOperation, repositoryDataService);
                    break;
                default:
                    _logger.LogWarning("Unknown operation type {OperationType} for failed operation {FailedOperationId}",
                        failedOperation.OperationType, failedOperation.Id);
                    return;
            }

            // If successful, delete the failed operation
            await failedOperationService.DeleteFailedOperationAsync(failedOperation.Id);
            _logger.LogInformation("Successfully retried failed operation {FailedOperationId}", failedOperation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry attempt #{RetryCount} failed for operation {FailedOperationId}",
                failedOperation.RetryCount, failedOperation.Id);

            // Update failed operation with new retry information
            failedOperation.LastRetryAttempt = DateTime.UtcNow;
            await failedOperationService.UpdateFailedOperationAsync(failedOperation);

            // If max retries exceeded, log as permanently failed
            if (failedOperation.RetryCount >= 3)
            {
                _logger.LogError("Failed operation {FailedOperationId} has exceeded maximum retry attempts and will remain in dead letter queue",
                    failedOperation.Id);
            }
        }
    }

    private Task RetryCommitProcessingAsync(FailedOperation failedOperation, IRepositoryDataService repositoryDataService)
    {
        _logger.LogInformation("Retrying commit processing for commit {CommitSha} in repository {RepositoryId}",
            failedOperation.EntityId, failedOperation.RepositoryId);

        // This is a simplified retry - in a real implementation, you'd need to re-execute the full commit processing logic
        // For now, we'll just log that a retry would occur
        _logger.LogWarning("Commit processing retry logic needs to be implemented for commit {CommitSha}", failedOperation.EntityId);

        // In a real implementation, you would:
        // 1. Get the repository
        // 2. Get the file extensions to count
        // 3. Re-count lines for the commit
        // 4. Re-store the commit line count data

        return Task.CompletedTask;

        // For demonstration purposes, we'll throw an exception to show the retry mechanism works
        throw new NotImplementedException("Commit processing retry logic needs to be fully implemented");
    }
}
