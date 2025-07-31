# Error Handling and Retry Mechanisms

This document describes the error handling and retry mechanisms implemented in the PoRepoLineTracker application.

## Overview

The application implements a comprehensive error handling strategy with dead letter queue functionality for failed operations. This includes:
- Automatic recording of failed operations
- Retry mechanisms with exponential backoff
- Background processing of failed operations
- API endpoints for monitoring and management

## Failed Operation Tracking

### Domain Model
The `FailedOperation` domain model tracks all failed operations:
- **Id**: Unique identifier for the failed operation
- **RepositoryId**: Associated repository
- **OperationType**: Type of operation that failed (e.g., "CommitProcessing")
- **EntityId**: Specific entity that failed (e.g., commit SHA)
- **ErrorMessage**: Error message from the failure
- **StackTrace**: Full stack trace for debugging
- **FailedAt**: Timestamp when the failure occurred
- **RetryCount**: Number of retry attempts
- **LastRetryAttempt**: Timestamp of last retry attempt
- **ContextData**: Additional context for retry processing

### Storage
Failed operations are stored in Azure Table Storage using the `FailedOperationEntity` infrastructure model. The table name is configurable via `AzureTableStorage:FailedOperationTableName`.

## Retry Mechanisms

### Exponential Backoff
The retry system implements exponential backoff:
- First retry: 5 minutes after failure
- Second retry: 10 minutes after first retry
- Third retry: 20 minutes after second retry
- Maximum of 3 retry attempts

### Background Processing
The `FailedOperationBackgroundService` runs as a hosted service that:
1. Polls for retryable operations every 5 minutes
2. Processes each failed operation with retry logic
3. Updates retry tracking information
4. Removes successfully processed operations from the dead letter queue

### Retry Logic Implementation
```csharp
// In AnalyzeRepositoryCommitsCommandHandler.cs
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing commit {CommitSha} for repository {RepositoryId}", commitStat.Sha, request.RepositoryId);
    
    // Record failed operation for retry or analysis
    var failedOperation = new FailedOperation
    {
        Id = Guid.NewGuid(),
        RepositoryId = request.RepositoryId,
        OperationType = "CommitProcessing",
        EntityId = commitStat.Sha,
        ErrorMessage = ex.Message,
        StackTrace = ex.StackTrace ?? string.Empty,
        FailedAt = DateTime.UtcNow,
        RetryCount = 0,
        ContextData = new Dictionary<string, object>
        {
            { "LocalPath", localPath },
            { "CommitDate", commitStat.CommitDate },
            { "LinesAdded", commitStat.LinesAdded },
            { "LinesRemoved", commitStat.LinesRemoved }
        }
    };

    await _failedOperationService.RecordFailedOperationAsync(failedOperation);
}
```

## API Endpoints

### Get Failed Operations
```http
GET /api/failed-operations/{repositoryId}
```
Retrieves all failed operations for a specific repository.

### Delete Failed Operation
```http
DELETE /api/failed-operations/{failedOperationId}
```
Manually removes a failed operation from the dead letter queue.

## Configuration

### appsettings.json
```json
{
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "FailedOperationTableName": "PoRepoLineTrackerFailedOperations"
  }
}
```

## Best Practices

### Error Logging
All errors are logged with appropriate context:
- Repository ID for repository-level operations
- Commit SHA for commit-level operations
- Full stack traces for debugging
- Structured logging with Serilog

### Graceful Degradation
The system continues processing other operations even when individual operations fail:
- Failed commits don't stop repository analysis
- Failed repositories don't stop bulk operations
- Background retry processing is independent of main processing

### Monitoring
Failed operations can be monitored through:
- Log analysis
- API endpoints for failed operation retrieval
- Health check endpoints that include failed operation service status

## Future Enhancements

### Advanced Retry Strategies
- Configurable retry policies per operation type
- Circuit breaker patterns for external service failures
- Priority-based processing for critical operations

### Enhanced Monitoring
- Metrics collection for failed operation rates
- Alerting for high failure rates
- Dashboard for failed operation visualization

### Improved Retry Logic
- Operation-specific retry implementations
- Context-aware retry decisions
- Manual retry triggers through API
