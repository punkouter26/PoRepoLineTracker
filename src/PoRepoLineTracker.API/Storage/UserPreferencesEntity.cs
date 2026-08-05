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
    /// Preferred chart rendering mode.
    /// </summary>
    public string ChartDisplayMode { get; set; } = Shared.Domain.ChartDisplayMode.TrueData.ToString();

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
        ChartDisplayMode = prefs.ChartDisplayMode.ToString();
        LastUpdated = prefs.LastUpdated;
    }

    public PoRepoLineTracker.Shared.Domain.UserPreferences ToDomainModel()
    {
        var chartDisplayMode = Enum.TryParse<PoRepoLineTracker.Shared.Domain.ChartDisplayMode>(ChartDisplayMode, true, out var parsedChartDisplayMode)
            ? parsedChartDisplayMode
            : PoRepoLineTracker.Shared.Domain.ChartDisplayMode.TrueData;

        return new PoRepoLineTracker.Shared.Domain.UserPreferences
        {
            UserId = new PoRepoLineTracker.Shared.Domain.UserId(UserId),
            FileExtensions = string.IsNullOrEmpty(FileExtensions)
                ? PoRepoLineTracker.Shared.Domain.UserPreferences.DefaultFileExtensions
                : FileExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            ChartDisplayMode = chartDisplayMode,
            LastUpdated = LastUpdated
        };
    }
}
