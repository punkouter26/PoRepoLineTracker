using Azure;
using Azure.Data.Tables;
using PoRepoLineTracker.Domain.Models;
using System.Text.Json;

namespace PoRepoLineTracker.Infrastructure.Entities;

public class CommitLineCountEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // RepositoryId (as string)
    public string RowKey { get; set; } = default!;       // CommitSha
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
    public int TotalLines { get; set; }
    public string LinesByFileTypeJson { get; set; } = string.Empty; // Stored as JSON string

    public CommitLineCount ToDomainModel()
    {
        return new CommitLineCount
        {
            Id = Id,
            RepositoryId = RepositoryId,
            CommitSha = CommitSha,
            CommitDate = CommitDate,
            TotalLines = TotalLines,
            LinesByFileType = string.IsNullOrEmpty(LinesByFileTypeJson)
                ? new Dictionary<string, int>()
                : JsonSerializer.Deserialize<Dictionary<string, int>>(LinesByFileTypeJson) ?? new Dictionary<string, int>()
        };
    }

    public static CommitLineCountEntity FromDomainModel(CommitLineCount model)
    {
        return new CommitLineCountEntity
        {
            PartitionKey = model.RepositoryId.ToString(),
            RowKey = model.CommitSha,
            Id = model.Id,
            RepositoryId = model.RepositoryId,
            CommitSha = model.CommitSha,
            CommitDate = model.CommitDate,
            TotalLines = model.TotalLines,
            LinesByFileTypeJson = JsonSerializer.Serialize(model.LinesByFileType)
        };
    }
}
