using Azure;
using Azure.Data.Tables;
using PoRepoLineTracker.Domain.Models;
using System.Text.Json;

namespace PoRepoLineTracker.Infrastructure.Entities;

/// <summary>
/// Repository Pattern: Entity for storing failed operations in Azure Table Storage.
/// Implements ITableEntity for Azure Table Storage persistence.
/// </summary>
public class FailedOperationEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // RepositoryId (as string)
    public string RowKey { get; set; } = default!;       // OperationType + EntityId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastRetryAttempt { get; set; }
    public string ContextDataJson { get; set; } = string.Empty; // Stored as JSON string

    public FailedOperation ToDomainModel()
    {
        return new FailedOperation
        {
            Id = Id,
            RepositoryId = RepositoryId,
            OperationType = OperationType,
            EntityId = EntityId,
            ErrorMessage = ErrorMessage,
            StackTrace = StackTrace,
            FailedAt = FailedAt,
            RetryCount = RetryCount,
            LastRetryAttempt = LastRetryAttempt,
            ContextData = string.IsNullOrEmpty(ContextDataJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(ContextDataJson) ?? new Dictionary<string, object>()
        };
    }

    public static FailedOperationEntity FromDomainModel(FailedOperation model)
    {
        return new FailedOperationEntity
        {
            PartitionKey = model.RepositoryId.ToString(),
            RowKey = $"{model.OperationType}_{model.EntityId}",
            Id = model.Id,
            RepositoryId = model.RepositoryId,
            OperationType = model.OperationType,
            EntityId = model.EntityId,
            ErrorMessage = model.ErrorMessage,
            StackTrace = model.StackTrace,
            FailedAt = model.FailedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(model.FailedAt, DateTimeKind.Utc)
                : model.FailedAt.ToUniversalTime(),
            RetryCount = model.RetryCount,
            LastRetryAttempt = model.LastRetryAttempt?.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(model.LastRetryAttempt.Value, DateTimeKind.Utc)
                : model.LastRetryAttempt?.ToUniversalTime(),
            ContextDataJson = JsonSerializer.Serialize(model.ContextData)
        };
    }
}
