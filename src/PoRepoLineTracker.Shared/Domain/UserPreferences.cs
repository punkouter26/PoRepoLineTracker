namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// Represents user-specific preferences for repository analysis.
/// </summary>
public record UserPreferences
{
    /// <summary>
    /// User ID this preference belongs to.
    /// </summary>
    public UserId UserId { get; init; }

    /// <summary>
    /// File extensions to include in line count calculations.
    /// Example: [".cs", ".razor", ".js", ".ts"]
    /// </summary>
    public List<string> FileExtensions { get; init; } = DefaultFileExtensions;

    /// <summary>
    /// Default file extensions for new users.
    /// Tuned for a typical Blazor WebAssembly + .NET API codebase:
    ///   .NET (server + Razor) + modern JS/TS frontend tooling + Python.
    /// Excludes generated/lock/config files on purpose (see FileIgnoreFilter).
    /// </summary>
    public static List<string> DefaultFileExtensions =>
    [
        // .NET server / Razor
        ".cs", ".razor", ".cshtml", ".xaml", ".csproj",
        // Modern JS/TS frontend
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
        // Web markup & styling
        ".html", ".css", ".scss", ".less",
        // Python
        ".py"
    ];

    /// <summary>
    /// When the user last opened the app, as recorded by the digest endpoint's "mark seen" call.
    ///
    /// <para>Null for a user who has never been marked seen — which is every user before this
    /// field existed, not only genuinely new ones. The digest treats null as "no last visit" and
    /// falls back to a trailing 7-day window rather than reporting the whole of history as news.</para>
    ///
    /// <para>It lives on preferences rather than on the user row because it is per-user display
    /// state that the client owns the timing of, exactly like the counted-extensions list, and
    /// preferences already have a read/write path the client is authenticated against.</para>
    /// </summary>
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>
    /// When the preferences were last updated.
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}
