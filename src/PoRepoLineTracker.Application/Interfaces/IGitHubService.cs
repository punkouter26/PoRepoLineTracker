using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IGitHubService
{
    Task<string> CloneRepositoryAsync(string repoUrl, string localPath, string? accessToken = null);
    Task<string> PullRepositoryAsync(string localPath, string? accessToken = null);
    Task<bool> IsRepositoryValidAsync(string localPath);

    /// <summary>
    /// Checks if a repository is valid using its full path (for locally uploaded repositories).
    /// </summary>
    Task<bool> IsLocalRepositoryValidAsync(string fullPath);

    /// <summary>
    /// Gets all commits from a local repository at its full path, optionally since a specific date.
    /// Used for locally uploaded repositories.
    /// </summary>
    Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsFromFullPathAsync(string fullPath, DateTime? sinceDate = null);

    /// <summary>
    /// Gets commit stats from a local repository at its full path, optionally since a specific date.
    /// Used for locally uploaded repositories.
    /// </summary>
    Task<IEnumerable<CommitStatsDto>> GetCommitStatsFromFullPathAsync(string fullPath, DateTime? sinceDate = null);

    /// <summary>
    /// Counts lines in a commit for a local repository at its full path.
    /// Used for locally uploaded repositories.
    /// </summary>
    Task<Dictionary<string, int>> CountLinesInCommitFromFullPathAsync(string fullPath, string commitSha, IEnumerable<string> fileExtensionsToCount);

    /// <summary>
    /// Gets top files by line count from a local repository at its full path.
    /// Used for locally uploaded repositories.
    /// </summary>
    Task<IEnumerable<TopFileDto>> GetTopFilesByLineCountFromFullPathAsync(string fullPath, IEnumerable<string> fileExtensionsToCount, int count = 5);

    /// <summary>
    /// Deletes the local repository directory so it can be re-cloned from scratch.
    /// </summary>
    Task DeleteLocalRepositoryAsync(string localPath);
    Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsAsync(string localPath, DateTime? sinceDate = null);
    Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount);
    Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string localPath, DateTime? sinceDate = null);
    Task<long> GetTotalLinesOfCodeAsync(string localPath, IEnumerable<string> fileExtensionsToCount);
    Task<IEnumerable<TopFileDto>> GetTopFilesByLineCountAsync(string localPath, IEnumerable<string> fileExtensionsToCount, int count = 5);
    Task CheckConnectionAsync();
    Task<IEnumerable<GitHubUserRepositoryDto>> GetUserRepositoriesAsync(string accessToken);

    /// <summary>
    /// Gets file contents from a commit for AI detection analysis.
    /// </summary>
    /// <param name="localPath">The local path of the repository.</param>
    /// <param name="commitSha">The commit SHA.</param>
    /// <param name="fileExtensionsToCount">File extensions to include in the analysis.</param>
    /// <returns>Dictionary of file paths to their content.</returns>
    Task<Dictionary<string, string>> GetFileContentsFromCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount);

    /// <summary>
    /// Gets file contents from a commit using full path (for locally uploaded repos).
    /// </summary>
    Task<Dictionary<string, string>> GetFileContentsFromCommitFullPathAsync(string fullPath, string commitSha, IEnumerable<string> fileExtensionsToCount);
}
