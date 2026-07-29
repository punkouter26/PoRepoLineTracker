using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Telemetry;

/// <summary>
/// Rule 6.1 — source-generated, allocation-free logging for the high-frequency commit
/// analysis loop. [LoggerMessage] emits the logging plumbing at compile time, eliminating
/// the value boxing and message-template parsing that the ILogger.LogDebug extensions incur
/// on every one of (potentially) thousands of per-commit calls.
/// </summary>
public static partial class AnalysisLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Force re-analyzing commit {CommitSha} with missing diff data")]
    public static partial void ForceReanalyzingCommit(this ILogger logger, string commitSha);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Commit {CommitSha} already has diff data, skipping")]
    public static partial void CommitAlreadyHasDiff(this ILogger logger, string commitSha);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Commit {CommitSha} already processed, skipping")]
    public static partial void CommitAlreadyProcessed(this ILogger logger, string commitSha);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed commit {CommitSha} with {TotalLines} lines (Added: {LinesAdded}, Removed: {LinesRemoved})")]
    public static partial void ProcessedCommit(this ILogger logger, string commitSha, int totalLines, int linesAdded, int linesRemoved);
}
