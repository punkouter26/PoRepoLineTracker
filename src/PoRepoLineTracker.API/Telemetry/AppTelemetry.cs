using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PoRepoLineTracker.API.Telemetry;

/// <summary>
/// Centralized telemetry instrumentation for the Application layer.
/// Defines ActivitySource for distributed tracing and Meter for custom metrics.
/// </summary>
public static class AppTelemetry
{
    public const string SourceName = "PoRepoLineTracker";
    public const string Version = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(SourceName, Version);
    public static readonly Meter Meter = new(SourceName, Version);

    // Counters
    public static readonly Counter<long> RepositoriesAdded = Meter.CreateCounter<long>(
        "repositories.added",
        unit: "{repository}",
        description: "Total number of repositories added to the system");

    public static readonly Counter<long> CommitsAnalyzed = Meter.CreateCounter<long>(
        "commits.analyzed",
        unit: "{commit}",
        description: "Total number of commits analyzed");

    public static readonly Counter<long> LinesAnalyzed = Meter.CreateCounter<long>(
        "code.lines_analyzed",
        unit: "{line}",
        description: "Total lines of code analyzed");

    public static readonly Counter<long> RepositoryClones = Meter.CreateCounter<long>(
        "repositories.cloned",
        unit: "{repository}",
        description: "Total repository clone operations");

    public static readonly Counter<long> FailedOperations = Meter.CreateCounter<long>(
        "operations.failed",
        unit: "{operation}",
        description: "Total number of failed operations (analysis, clone, storage)");

    // Histograms
    public static readonly Histogram<double> AddRepositoryDuration = Meter.CreateHistogram<double>(
        "repository.add_duration",
        unit: "ms",
        description: "Duration of add repository operations");

    public static readonly Histogram<double> AnalysisDuration = Meter.CreateHistogram<double>(
        "analysis.duration",
        unit: "ms",
        description: "Duration of repository commit analysis operations");

    public static readonly Histogram<double> CloneDuration = Meter.CreateHistogram<double>(
        "clone.duration",
        unit: "ms",
        description: "Duration of repository clone operations");

    public static readonly Histogram<double> CommitProcessingDuration = Meter.CreateHistogram<double>(
        "commit.processing_duration",
        unit: "ms",
        description: "Duration of individual commit processing");

    public static readonly Histogram<double> GitHubApiLatency = Meter.CreateHistogram<double>(
        "github_api.latency",
        unit: "ms",
        description: "GitHub API response latency for performance monitoring");

    public static readonly Counter<long> GitHubApiCalls = Meter.CreateCounter<long>(
        "github_api.calls",
        unit: "{request}",
        description: "Total GitHub API calls made by the application");

    // Observable gauges — initialized at startup via InitializeGauges()
    public static ObservableGauge<int>? TotalRepositories { get; private set; }
    public static ObservableGauge<int>? PendingAnalysis { get; private set; }
    public static ObservableGauge<int>? GitHubRateLimitRemaining { get; private set; }
    public static ObservableGauge<long>? TotalLinesOfCode { get; private set; }

    public static void InitializeGauges(
        Func<int> getTotalRepositories,
        Func<int> getPendingAnalysis,
        Func<int>? getGitHubRateLimit = null,
        Func<long>? getTotalLinesOfCode = null)
    {
        TotalRepositories = Meter.CreateObservableGauge(
            "repositories.total", getTotalRepositories,
            unit: "{repository}", description: "Total number of repositories in the system");

        PendingAnalysis = Meter.CreateObservableGauge(
            "repositories.pending_analysis", getPendingAnalysis,
            unit: "{repository}", description: "Number of repositories pending analysis");

        if (getGitHubRateLimit != null)
            GitHubRateLimitRemaining = Meter.CreateObservableGauge(
                "github_api.rate_limit_remaining", getGitHubRateLimit,
                unit: "{requests}", description: "Remaining GitHub API calls before hitting rate limit");

        if (getTotalLinesOfCode != null)
            TotalLinesOfCode = Meter.CreateObservableGauge(
                "code.total_lines", getTotalLinesOfCode,
                unit: "{lines}", description: "Total lines of code across all repositories");
    }
}
