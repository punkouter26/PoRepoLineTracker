using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IGitHubService
{
    Task<string> CloneRepositoryAsync(string repoUrl, string localPath, string? accessToken = null);
    Task<string> PullRepositoryAsync(string localPath, string? accessToken = null);
    Task<bool> IsRepositoryValidAsync(string localPath);
    /// <summary>Deletes the local repository directory so it can be re-cloned from scratch.</summary>
    Task DeleteLocalRepositoryAsync(string localPath);
    Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsAsync(string localPath, DateTime? sinceDate = null);
    Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount);
    Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string localPath, DateTime? sinceDate = null);
    Task<long> GetTotalLinesOfCodeAsync(string localPath, IEnumerable<string> fileExtensionsToCount);
    Task<IEnumerable<TopFileDto>> GetTopFilesByLineCountAsync(string localPath, IEnumerable<string> fileExtensionsToCount, int count = 5);
    Task CheckConnectionAsync();
    Task<IEnumerable<GitHubUserRepositoryDto>> GetUserRepositoriesAsync(string accessToken);
}
