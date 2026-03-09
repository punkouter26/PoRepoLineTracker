using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PoRepoLineTracker.Application.Telemetry;

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
}
