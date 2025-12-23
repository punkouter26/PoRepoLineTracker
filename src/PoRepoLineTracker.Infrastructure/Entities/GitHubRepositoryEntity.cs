using Azure;
using Azure.Data.Tables;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Infrastructure.Entities;

/// <summary>
/// Azure Table Storage entity for GitHubRepository.
/// PartitionKey: UserId (partitions repositories by user for multi-tenancy)
/// RowKey: Owner_Name (unique identifier for the repo within a user's collection)
/// </summary>
public class GitHubRepositoryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // UserId (for multi-user partitioning)
    public string RowKey { get; set; } = default!;       // Owner_Name
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public DateTime? LastAnalyzedCommitDate { get; set; } // Nullable to avoid Azure Table Storage DateTime.MinValue issue

    public GitHubRepository ToDomainModel()
    {
        return new GitHubRepository
        {
            Id = Id,
            UserId = UserId,
            Owner = Owner,
            Name = Name,
            CloneUrl = CloneUrl,
            LastAnalyzedCommitDate = LastAnalyzedCommitDate
        };
    }

    public static GitHubRepositoryEntity FromDomainModel(GitHubRepository model)
    {
        return new GitHubRepositoryEntity
        {
            PartitionKey = model.UserId.ToString(),
            RowKey = $"{model.Owner}_{model.Name}",
            Id = model.Id,
            UserId = model.UserId,
            Owner = model.Owner,
            Name = model.Name,
            CloneUrl = model.CloneUrl,
            LastAnalyzedCommitDate = model.LastAnalyzedCommitDate.HasValue && model.LastAnalyzedCommitDate.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(model.LastAnalyzedCommitDate.Value, DateTimeKind.Utc)
                : model.LastAnalyzedCommitDate?.ToUniversalTime()
        };
    }
}

