namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// Strategy Pattern: Represents a failed operation that can be retried or analyzed.
/// Used for implementing dead letter queue pattern for failed commit processing.
/// </summary>
public class FailedOperation
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string OperationType { get; set; } = string.Empty; // e.g., "CommitProcessing", "RepositoryAnalysis"
    public string EntityId { get; set; } = string.Empty; // e.g., commit SHA, repository ID
    public string ErrorMessage { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastRetryAttempt { get; set; }
    public Dictionary<string, object> ContextData { get; set; } = new(); // Additional context for retry
}
