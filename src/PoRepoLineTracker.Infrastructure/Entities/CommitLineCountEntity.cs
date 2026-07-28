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
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public string LinesByFileTypeJson { get; set; } = string.Empty; // Stored as JSON string

    // Author information
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;

    // AI detection result
    public double AiPercentage { get; set; }

    // CommitTagger: JSON-serialized list of tags
    public string TagsJson { get; set; } = string.Empty;

    public CommitLineCount ToDomainModel()
    {
        return new CommitLineCount
        {
            Id = Id,
            RepositoryId = new RepositoryId(RepositoryId),
            CommitSha = CommitSha,
            CommitDate = CommitDate,
            TotalLines = TotalLines,
            LinesAdded = LinesAdded,
            LinesRemoved = LinesRemoved,
            LinesByFileType = string.IsNullOrEmpty(LinesByFileTypeJson)
                ? new Dictionary<string, int>()
                : JsonSerializer.Deserialize<Dictionary<string, int>>(LinesByFileTypeJson) ?? new Dictionary<string, int>(),
            AuthorName = AuthorName,
            AuthorEmail = AuthorEmail,
            AiPercentage = AiPercentage,
            Tags = string.IsNullOrEmpty(TagsJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(TagsJson) ?? new List<string>()
        };
    }

    public static CommitLineCountEntity FromDomainModel(CommitLineCount model)
    {
        return new CommitLineCountEntity
        {
            PartitionKey = model.RepositoryId.ToString(),
            RowKey = model.CommitSha,
            Id = model.Id,
            RepositoryId = model.RepositoryId.Value,
            CommitSha = model.CommitSha,
            CommitDate = model.CommitDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(model.CommitDate, DateTimeKind.Utc)
                : model.CommitDate.ToUniversalTime(),
            TotalLines = model.TotalLines,
            LinesAdded = model.LinesAdded,
            LinesRemoved = model.LinesRemoved,
            LinesByFileTypeJson = JsonSerializer.Serialize(model.LinesByFileType),
            AuthorName = model.AuthorName,
            AuthorEmail = model.AuthorEmail,
            AiPercentage = model.AiPercentage,
            TagsJson = JsonSerializer.Serialize(model.Tags)
        };
    }
}
