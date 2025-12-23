using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PoRepoLineTracker.Api.Telemetry;

/// <summary>
/// Provides centralized telemetry instrumentation for the application.
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

    /// <summary>
    /// Counter for repository clone operations.
    /// Tags: status (success/failure)
    /// </summary>
    public static readonly Counter<long> RepositoryClones = Meter.CreateCounter<long>(
        "repositories.cloned",
        unit: "{repository}",
        description: "Total repository clone operations");

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
    /// Histogram for repository clone duration.
    /// Tracks how long it takes to clone a repository.
    /// Tags: repository_owner, repository_name, size_category (small/medium/large)
    /// </summary>
    public static readonly Histogram<double> CloneDuration = Meter.CreateHistogram<double>(
        "clone.duration",
        unit: "ms",
        description: "Duration of repository clone operations");

    /// <summary>
    /// Histogram for commit processing duration.
    /// Tracks how long it takes to process a single commit.
    /// Tags: has_file_changes (true/false)
    /// </summary>
    public static readonly Histogram<double> CommitProcessingDuration = Meter.CreateHistogram<double>(
        "commit.processing_duration",
        unit: "ms",
        description: "Duration of individual commit processing");

    // ===== Gauges (Observable) =====

    /// <summary>
    /// Observable gauge for total repositories in the system.
    /// Updated periodically to reflect current count.
    /// </summary>
    public static ObservableGauge<int>? TotalRepositories { get; set; }

    /// <summary>
    /// Observable gauge for repositories pending analysis.
    /// Shows how many repositories are waiting to be analyzed.
    /// </summary>
    public static ObservableGauge<int>? PendingAnalysis { get; set; }

    /// <summary>
    /// Initializes observable gauges with callback functions.
    /// Call this during application startup after services are configured.
    /// </summary>
    /// <param name="getTotalRepositories">Callback to get total repository count</param>
    /// <param name="getPendingAnalysis">Callback to get pending analysis count</param>
    public static void InitializeGauges(
        Func<int> getTotalRepositories,
        Func<int> getPendingAnalysis)
    {
        TotalRepositories = Meter.CreateObservableGauge(
            "repositories.total",
            getTotalRepositories,
            unit: "{repository}",
            description: "Total number of repositories in the system");

        PendingAnalysis = Meter.CreateObservableGauge(
            "repositories.pending_analysis",
            getPendingAnalysis,
            unit: "{repository}",
            description: "Number of repositories pending analysis");
    }
}
