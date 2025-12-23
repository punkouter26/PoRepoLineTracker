using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// Provides GitHub repository management operations including cloning, pulling,
/// commit analysis, and line counting.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Clones a GitHub repository to a local path.
    /// </summary>
    /// <param name="repoUrl">The HTTPS URL of the repository to clone.</param>
    /// <param name="localPath">The relative path within the local repos directory.</param>
    /// <param name="accessToken">Optional GitHub access token for private repositories.</param>
    /// <returns>The full local path where the repository was cloned.</returns>
    Task<string> CloneRepositoryAsync(string repoUrl, string localPath, string? accessToken = null);

    /// <summary>
    /// Pulls the latest changes for a repository at the specified local path.
    /// </summary>
    /// <param name="localPath">The relative path to the local repository.</param>
    /// <param name="accessToken">Optional GitHub access token for private repositories.</param>
    /// <returns>The full local path of the repository.</returns>
    Task<string> PullRepositoryAsync(string localPath, string? accessToken = null);

    /// <summary>
    /// Gets all commits from a repository, optionally since a specific date.
    /// </summary>
    /// <param name="localPath">The relative path to the local repository.</param>
    /// <param name="sinceDate">Optional date to filter commits (UTC). Only commits after this date are returned.</param>
    /// <returns>A collection of commit SHA and commit date tuples.</returns>
    Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsAsync(string localPath, DateTime? sinceDate = null);

    /// <summary>
    /// Counts the lines of code per file extension in a specific commit.
    /// </summary>
    /// <param name="localPath">The relative path to the local repository.</param>
    /// <param name="commitSha">The SHA of the commit to analyze.</param>
    /// <param name="fileExtensionsToCount">The file extensions to include (e.g., ".cs", ".js").</param>
    /// <returns>A dictionary mapping file extensions to line counts.</returns>
    Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount);

    /// <summary>
    /// Gets detailed commit statistics for a repository.
    /// </summary>
    /// <param name="localPath">The relative path to the local repository.</param>
    /// <param name="sinceDate">Optional date to filter commits (UTC).</param>
    /// <returns>A collection of commit statistics DTOs.</returns>
    Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string localPath, DateTime? sinceDate = null);

    /// <summary>
    /// Gets the total lines of code in the current state of the repository.
    /// </summary>
    /// <param name="localPath">The relative path to the local repository.</param>
    /// <param name="fileExtensionsToCount">The file extensions to include.</param>
    /// <returns>The total line count across all matching files.</returns>
    Task<long> GetTotalLinesOfCodeAsync(string localPath, IEnumerable<string> fileExtensionsToCount);

    /// <summary>
    /// Gets the top N files by line count in the current state of the repository.
    /// </summary>
    /// <param name="localPath">The relative path to the local repository.</param>
    /// <param name="fileExtensionsToCount">The file extensions to include.</param>
    /// <param name="count">Number of top files to return.</param>
    /// <returns>A collection of file names and line counts.</returns>
    Task<IEnumerable<TopFileDto>> GetTopFilesByLineCountAsync(string localPath, IEnumerable<string> fileExtensionsToCount, int count = 5);

    /// <summary>
    /// Checks connectivity to the GitHub API.
    /// </summary>
    /// <exception cref="Exception">Thrown when the connection check fails.</exception>
    Task CheckConnectionAsync();

    /// <summary>
    /// Gets repositories accessible to the authenticated user.
    /// </summary>
    /// <param name="accessToken">The GitHub OAuth access token for the user.</param>
    /// <returns>A collection of user repositories with metadata.</returns>
    Task<IEnumerable<GitHubUserRepository>> GetUserRepositoriesAsync(string accessToken);
}
