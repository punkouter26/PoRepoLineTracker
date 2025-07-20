using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

public interface IRepositoryService
{
    Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync();
    Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl);
    Task AnalyzeRepositoryCommitsAsync(Guid repositoryId);
    Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId);
}
