
namespace PoRepoLineTracker.API.Storage;

public interface IGitHubService
{
    /// <summary>
    /// Base directory local repository clones live under — Azure App Service ephemeral storage
    /// when running there, otherwise the configured or default local path. The single source for
    /// resolving a repo's on-disk location; do not re-derive this from the HOME environment
    /// variable elsewhere.
    /// </summary>
    string LocalReposBasePath { get; }

    Task<string> CloneRepositoryAsync(string repoUrl, string localPath, string? accessToken = null);
    Task<string> PullRepositoryAsync(string localPath, string? accessToken = null);
    Task<bool> IsRepositoryValidAsync(string localPath);

    /// <summary>
    /// Checks if a repository is valid using its full path (for locally uploaded repositories).
    /// </summary>
    Task<bool> IsLocalRepositoryValidAsync(string fullPath);

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
    Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount);
    Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string localPath, DateTime? sinceDate = null);
    Task<IEnumerable<TopFileDto>> GetTopFilesByLineCountAsync(string localPath, IEnumerable<string> fileExtensionsToCount, int count = 5);
    Task CheckConnectionAsync();
    Task<IEnumerable<GitHubUserRepositoryDto>> GetUserRepositoriesAsync(string accessToken);
}
