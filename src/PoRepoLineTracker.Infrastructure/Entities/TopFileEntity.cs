using Azure;
using Azure.Data.Tables;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Infrastructure.Entities;

/// <summary>
/// Azure Table Storage entity for storing top files by line count.
/// Partition key is the repository ID, row key is the file name (URL-encoded to handle special chars).
/// </summary>
public class TopFileEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // RepositoryId (as string)
    public string RowKey { get; set; } = default!;       // Rank (1-5) to maintain order
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid RepositoryId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public int Rank { get; set; } // 1-based rank (1 = highest line count)

    public TopFileDto ToDto()
    {
        return new TopFileDto
        {
            FileName = FileName,
            LineCount = LineCount
        };
    }

    public static TopFileEntity FromDto(Guid repositoryId, TopFileDto dto, int rank)
    {
        return new TopFileEntity
        {
            PartitionKey = repositoryId.ToString(),
            RowKey = rank.ToString("D3"), // Pad to 3 digits for proper sorting (001, 002, etc.)
            RepositoryId = repositoryId,
            FileName = dto.FileName,
            LineCount = dto.LineCount,
            Rank = rank
        };
    }
}
