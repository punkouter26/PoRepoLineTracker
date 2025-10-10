using Azure;
using Azure.Data.Tables;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Infrastructure.Entities;

public class GitHubRepositoryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // Owner
    public string RowKey { get; set; } = default!;       // RepoName
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid Id { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public DateTime? LastAnalyzedCommitDate { get; set; } // Nullable to avoid Azure Table Storage DateTime.MinValue issue

    public GitHubRepository ToDomainModel()
    {
        return new GitHubRepository
        {
            Id = Id,
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
            PartitionKey = model.Owner,
            RowKey = model.Name,
            Id = model.Id,
            Owner = model.Owner,
            Name = model.Name,
            CloneUrl = model.CloneUrl,
            LastAnalyzedCommitDate = model.LastAnalyzedCommitDate.HasValue && model.LastAnalyzedCommitDate.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(model.LastAnalyzedCommitDate.Value, DateTimeKind.Utc)
                : model.LastAnalyzedCommitDate?.ToUniversalTime()
        };
    }
}
