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
    /// Controls how charts render line history data.
    /// </summary>
    public ChartDisplayMode ChartDisplayMode { get; init; } = ChartDisplayMode.TrueData;

    /// <summary>
    /// Default file extensions for new users.
    /// Tuned for a typical Blazor WebAssembly + .NET API codebase:
    ///   .NET (server + Razor) + modern JS/TS frontend tooling.
    /// Excludes generated/lock/config files on purpose (see FileIgnoreFilter).
    /// </summary>
    public static List<string> DefaultFileExtensions =>
    [
        // .NET server / Razor
        ".cs", ".razor", ".cshtml", ".xaml", ".csproj",
        // Modern JS/TS frontend
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
        // Web markup & styling
        ".html", ".css", ".scss", ".less"
    ];

    /// <summary>
    /// When the preferences were last updated.
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}
