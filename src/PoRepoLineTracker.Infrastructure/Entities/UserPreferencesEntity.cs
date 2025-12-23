using Azure;
using Azure.Data.Tables;

namespace PoRepoLineTracker.Infrastructure.Entities;

/// <summary>
/// Azure Table Storage entity for user preferences.
/// PartitionKey: "PREFS"
/// RowKey: UserId
/// </summary>
public class UserPreferencesEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "PREFS";
    public string RowKey { get; set; } = string.Empty; // UserId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// User ID (same as RowKey for querying).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Comma-separated list of file extensions.
    /// </summary>
    public string FileExtensions { get; set; } = string.Empty;

    /// <summary>
    /// When preferences were last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; }

    public UserPreferencesEntity() { }

    public UserPreferencesEntity(PoRepoLineTracker.Domain.Models.UserPreferences prefs)
    {
        PartitionKey = "PREFS";
        RowKey = prefs.UserId.ToString();
        UserId = prefs.UserId;
        FileExtensions = string.Join(",", prefs.FileExtensions);
        LastUpdated = prefs.LastUpdated;
    }

    public PoRepoLineTracker.Domain.Models.UserPreferences ToDomainModel()
    {
        return new PoRepoLineTracker.Domain.Models.UserPreferences
        {
            UserId = UserId,
            FileExtensions = string.IsNullOrEmpty(FileExtensions) 
                ? PoRepoLineTracker.Domain.Models.UserPreferences.DefaultFileExtensions 
                : FileExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            LastUpdated = LastUpdated
        };
    }
}
