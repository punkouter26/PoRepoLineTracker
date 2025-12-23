namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// Represents user-specific preferences for repository analysis.
/// </summary>
public record UserPreferences
{
    /// <summary>
    /// User ID this preference belongs to.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// File extensions to include in line count calculations.
    /// Example: [".cs", ".razor", ".js", ".ts"]
    /// </summary>
    public List<string> FileExtensions { get; init; } = DefaultFileExtensions;

    /// <summary>
    /// Default file extensions for new users.
    /// </summary>
    public static List<string> DefaultFileExtensions =>
    [
        ".cs", ".razor", ".cshtml", ".xaml",
        ".js", ".jsx", ".ts", ".tsx",
        ".html", ".css", ".scss", ".less"
    ];

    /// <summary>
    /// When the preferences were last updated.
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}
