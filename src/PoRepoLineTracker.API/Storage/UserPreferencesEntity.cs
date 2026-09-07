using Azure;
using Azure.Data.Tables;

namespace PoRepoLineTracker.API.Storage;

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
    /// When the user last opened the app. Null on rows written before this column existed, which
    /// the digest reads as "no recorded visit" — see <see cref="Domain.Models.UserPreferences.LastSeenUtc"/>.
    ///
    /// <para>Nullable rather than defaulted to <see cref="DateTime.MinValue"/>: Table Storage
    /// rejects a DateTime below its own 1601 floor, so a non-nullable field with the default CLR
    /// value would fail the very first write for a user who has never been marked seen.</para>
    /// </summary>
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>
    /// When preferences were last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; }

    public UserPreferencesEntity() { }

    public UserPreferencesEntity(PoRepoLineTracker.Shared.Domain.UserPreferences prefs)
    {
        PartitionKey = "PREFS";
        RowKey = prefs.UserId.ToString();
        UserId = prefs.UserId.Value;
        FileExtensions = string.Join(",", prefs.FileExtensions);
        // Table Storage stores DateTime as UTC and round-trips anything else with its offset
        // applied, so a Local or Unspecified kind here would come back shifted and make the digest
        // window start at the wrong instant.
        LastSeenUtc = prefs.LastSeenUtc is { } seen
            ? (seen.Kind == DateTimeKind.Local ? seen.ToUniversalTime() : DateTime.SpecifyKind(seen, DateTimeKind.Utc))
            : null;
        LastUpdated = prefs.LastUpdated;
    }

    public PoRepoLineTracker.Shared.Domain.UserPreferences ToDomainModel()
    {
        return new PoRepoLineTracker.Shared.Domain.UserPreferences
        {
            UserId = new PoRepoLineTracker.Shared.Domain.UserId(UserId),
            FileExtensions = string.IsNullOrEmpty(FileExtensions)
                ? PoRepoLineTracker.Shared.Domain.UserPreferences.DefaultFileExtensions
                : FileExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            LastSeenUtc = LastSeenUtc,
            LastUpdated = LastUpdated
        };
    }
}
