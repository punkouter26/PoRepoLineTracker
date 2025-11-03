using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PoRepoLineTracker.Application.Telemetry;

/// <summary>
/// Provides centralized telemetry instrumentation for the application layer.
/// Defines ActivitySource for distributed tracing and Meter for custom metrics.
/// </summary>
public static class AppTelemetry
{
    /// <summary>
    /// The name of the telemetry source (matches application name).
    /// </summary>
    public const string SourceName = "PoRepoLineTracker";

    /// <summary>
    /// The version of the application.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// ActivitySource for creating custom trace spans.
    /// Use this to track distributed operations and performance.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary>
    /// Meter for creating custom metrics.
    /// Use this to track application-specific measurements.
    /// </summary>
    public static readonly Meter Meter = new(SourceName, Version);

    // ===== Counters =====
    
    /// <summary>
    /// Counter for total repositories added to the system.
    /// Tags: status (success/failure)
    /// </summary>
    public static readonly Counter<long> RepositoriesAdded = Meter.CreateCounter<long>(
        "repositories.added",
        unit: "{repository}",
        description: "Total number of repositories added to the system");

    /// <summary>
    /// Counter for total commits analyzed.
    /// Tags: repository_owner, repository_name, status (success/failure)
    /// </summary>
    public static readonly Counter<long> CommitsAnalyzed = Meter.CreateCounter<long>(
        "commits.analyzed",
        unit: "{commit}",
        description: "Total number of commits analyzed");

    /// <summary>
    /// Counter for total lines of code analyzed.
    /// Tags: language (e.g., .cs, .js, .py)
    /// </summary>
    public static readonly Counter<long> LinesAnalyzed = Meter.CreateCounter<long>(
        "code.lines_analyzed",
        unit: "{line}",
        description: "Total lines of code analyzed");

    // ===== Histograms =====
    
    /// <summary>
    /// Histogram for repository analysis duration.
    /// Tracks how long it takes to analyze a repository's commits.
    /// Tags: repository_owner, repository_name
    /// </summary>
    public static readonly Histogram<double> AnalysisDuration = Meter.CreateHistogram<double>(
        "analysis.duration",
        unit: "ms",
        description: "Duration of repository commit analysis operations");

    /// <summary>
    /// Histogram for repository add operation duration.
    /// Tags: owner, name
    /// </summary>
    public static readonly Histogram<double> AddRepositoryDuration = Meter.CreateHistogram<double>(
        "repository.add_duration",
        unit: "ms",
        description: "Duration of add repository operations");
}
