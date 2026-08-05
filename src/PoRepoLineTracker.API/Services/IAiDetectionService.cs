namespace PoRepoLineTracker.API.Services;

/// <summary>
/// Service for detecting AI-generated code patterns in source files.
/// Uses heuristic-based detection since perfect AI detection is not possible.
/// </summary>
public interface IAiDetectionService
{
    /// <summary>
    /// Analyzes a file's content and returns a percentage (0-100) indicating how likely it was AI-generated.
    /// </summary>
    /// <param name="content">The file content to analyze.</param>
    /// <param name="fileExtension">The file extension (e.g., ".cs", ".py").</param>
    /// <returns>A percentage from 0 (definitely human) to 100 (definitely AI).</returns>
    Task<double> AnalyzeContentAsync(string content, string fileExtension);

    /// <summary>
    /// Synchronous form of <see cref="AnalyzeContentAsync"/>.
    /// <para>
    /// The detection is CPU-bound regex work with no I/O, so the async form only ever wrapped it
    /// in <c>Task.Run</c>. The commit-scoring loop in <c>GitHubService</c> already runs inside one
    /// <c>Task.Run</c> over an open LibGit2Sharp repository — a handle that must not be touched
    /// from another thread — so it needs to score in place rather than block on a nested task.
    /// </para>
    /// </summary>
    double AnalyzeContent(string content, string fileExtension);

    /// <summary>
    /// Analyzes multiple files and returns the average AI detection percentage.
    /// </summary>
    /// <param name="files">Dictionary of file paths to their content.</param>
    /// <returns>Average AI percentage across all files.</returns>
    Task<double> AnalyzeMultipleFilesAsync(Dictionary<string, string> files);
}
