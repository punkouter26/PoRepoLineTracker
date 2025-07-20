using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IRepositoryService
{
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync();
    Task<GitHubRepoStatsDto?> GetRepositoryByOwnerAndNameAsync(string owner, string repoName);
    Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl);
    Task AnalyzeRepositoryCommitsAsync(Guid repositoryId);
    Task UpdateRepositoryAsync(GitHubRepository repository);
    Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId);
    Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days);
}
