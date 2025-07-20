using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Application.Services;

public class RepositoryService : IRepositoryService
{
    private readonly IGitHubService _gitHubService;
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<RepositoryService> _logger;

    private readonly IEnumerable<string> _fileExtensionsToCount = new[] { ".cs", ".js", ".ts", ".jsx", ".tsx", ".html", ".css", ".razor" };

    public RepositoryService(IGitHubService gitHubService, IRepositoryDataService repositoryDataService, ILogger<RepositoryService> logger)
    {
        _gitHubService = gitHubService;
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync()
    {
        _logger.LogInformation("Retrieving all repositories.");
        return await _repositoryDataService.GetAllRepositoriesAsync();
    }

    public async Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl)
    {
        _logger.LogInformation("Adding new repository: {Owner}/{RepoName}", owner, repoName);
        var newRepo = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            Owner = owner,
            Name = repoName,
            CloneUrl = cloneUrl,
            LastAnalyzedCommitDate = DateTime.MinValue // Initialize to min value to analyze all commits on first run
        };

        await _repositoryDataService.AddRepositoryAsync(newRepo);
        _logger.LogInformation("Repository {Owner}/{RepoName} added successfully.", owner, repoName);
        return newRepo;
    }

    public async Task AnalyzeRepositoryCommitsAsync(Guid repositoryId)
    {
        _logger.LogInformation("Starting analysis for repository ID: {RepositoryId}", repositoryId);
        var repository = await _repositoryDataService.GetRepositoryByIdAsync(repositoryId);

        if (repository == null)
        {
            _logger.LogWarning("Repository with ID {RepositoryId} not found for analysis.", repositoryId);
            return;
        }

        var localRepoPath = Path.Combine(repository.Owner, repository.Name); // Relative path within _localReposPath
        string actualLocalPath;

        try
        {
            // Try to pull first, if it fails (e.g., repo not cloned yet), then clone
            try
            {
                actualLocalPath = await _gitHubService.PullRepositoryAsync(localRepoPath);
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogInformation("Repository not found locally, cloning {RepoUrl} to {LocalPath}", repository.CloneUrl, localRepoPath);
                actualLocalPath = await _gitHubService.CloneRepositoryAsync(repository.CloneUrl, localRepoPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone or pull repository {RepoName}. Error: {ErrorMessage}", repository.Name, ex.Message);
            return;
        }

        var commitsWithDates = (await _gitHubService.GetCommitsAsync(actualLocalPath, repository.LastAnalyzedCommitDate))
                                .OrderBy(c => c.CommitDate) // Order by date to ensure LastAnalyzedCommitDate is correct
                                .ToList();
        _logger.LogInformation("Found {CommitCount} new commits to analyze for {RepoName}.", commitsWithDates.Count, repository.Name);

        DateTimeOffset latestCommitDate = repository.LastAnalyzedCommitDate;

        foreach (var commitInfo in commitsWithDates)
        {
            var commitSha = commitInfo.Sha;
            var commitDate = commitInfo.CommitDate;

            if (await _repositoryDataService.CommitExistsAsync(repository.Id, commitSha))
            {
                _logger.LogInformation("Commit {CommitSha} for repository {RepoName} already analyzed. Skipping.", commitSha, repository.Name);
                continue;
            }

            _logger.LogInformation("Analyzing commit {CommitSha} (Date: {CommitDate}) for repository {RepoName}.", commitSha, commitDate, repository.Name);
            var linesByFileType = await _gitHubService.CountLinesInCommitAsync(actualLocalPath, commitSha, _fileExtensionsToCount);
            var totalLines = linesByFileType.Sum(kv => kv.Value);

            var commitLineCount = new CommitLineCount
            {
                Id = Guid.NewGuid(),
                RepositoryId = repository.Id,
                CommitSha = commitSha,
                CommitDate = commitDate.DateTime, // Use the actual commit date
                TotalLines = totalLines,
                LinesByFileType = linesByFileType
            };

            await _repositoryDataService.AddCommitLineCountAsync(commitLineCount);
            _logger.LogInformation("Saved line count for commit {CommitSha}. Total lines: {TotalLines}", commitSha, totalLines);

            // Update latestCommitDate if this commit is newer
            if (commitDate > latestCommitDate)
            {
                latestCommitDate = commitDate;
            }
        }

        // Update LastAnalyzedCommitDate to the latest commit date processed
        if (latestCommitDate > repository.LastAnalyzedCommitDate)
        {
            repository.LastAnalyzedCommitDate = latestCommitDate.DateTime;
            await _repositoryDataService.UpdateRepositoryAsync(repository);
        }
        else
        {
            _logger.LogInformation("No new commits processed or latest commit date is not newer for repository ID: {RepositoryId}", repositoryId);
        }

        _logger.LogInformation("Analysis completed for repository ID: {RepositoryId}", repositoryId);
    }

    public async Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId)
    {
        _logger.LogInformation("Retrieving line counts for repository ID: {RepositoryId}", repositoryId);
        return await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repositoryId);
    }
}
