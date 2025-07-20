using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IGitHubService
{
    Task<string> CloneRepositoryAsync(string repoUrl, string localPath);
    Task<string> PullRepositoryAsync(string localPath);
    Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsAsync(string localPath, DateTime? sinceDate = null);
    Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount);
    Task CheckConnectionAsync();
}
