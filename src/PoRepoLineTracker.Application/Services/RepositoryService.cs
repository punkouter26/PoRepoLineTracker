using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Models;

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

    public async Task<GitHubRepoStatsDto?> GetRepositoryByOwnerAndNameAsync(string owner, string repoName)
    {
        _logger.LogInformation("Getting repository by Owner: {Owner} and Name: {RepoName} from Table Storage.", owner, repoName);
        var repo = await _repositoryDataService.GetRepositoryByOwnerAndNameAsync(owner, repoName);
        if (repo == null)
        {
            _logger.LogInformation("Repository {RepoName} by Owner {Owner} and Name {RepoName} not found.", repoName, owner, repoName);
            return null;
        }

        _logger.LogInformation("Found repository {RepoName} by Owner {Owner} and Name {RepoName}.", repoName, owner, repoName);

        var commitLineCounts = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repo.Id);

        var statsDto = new GitHubRepoStatsDto
        {
            Repository = repo,
            CommitCount = commitLineCounts.Count(),
            CommitLineCounts = commitLineCounts.ToList()
        };

        // Populate LineCounts (existing functionality)
        statsDto.LineCounts = commitLineCounts
            .SelectMany(clc => clc.LinesByFileType)
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

        return statsDto;
    }

    public async Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl)
    {
        _logger.LogInformation("Adding new repository: {Owner}/{RepoName}", owner, repoName);
        
        // Check if repository already exists
        // This check is now primarily for preventing duplicate additions from other parts of the app
        // The API endpoint will handle checking existence before calling AddRepositoryAsync
        var existingRepo = await _repositoryDataService.GetRepositoryByOwnerAndNameAsync(owner, repoName);
        if (existingRepo != null)
        {
            _logger.LogWarning("Repository {Owner}/{RepoName} already exists with ID: {RepoId}", owner, repoName, existingRepo.Id);
            throw new InvalidOperationException($"Repository '{owner}/{repoName}' already exists in the system.");
        }

        var newRepo = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            Owner = owner,
            Name = repoName,
            CloneUrl = cloneUrl,
            LastAnalyzedCommitDate = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) // Initialize to min value UTC to analyze all commits on first run
        };

        await _repositoryDataService.AddRepositoryAsync(newRepo);
        _logger.LogInformation("Repository {Owner}/{RepoName} added successfully.", owner, repoName);
        
        // Automatically analyze the repository after adding it
        _logger.LogInformation("Starting automatic analysis for newly added repository {Owner}/{RepoName}", owner, repoName);
        try
        {
            await AnalyzeRepositoryCommitsAsync(newRepo.Id);
            _logger.LogInformation("Automatic analysis completed for repository {Owner}/{RepoName}", owner, repoName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to automatically analyze repository {Owner}/{RepoName}: {ErrorMessage}", owner, repoName, ex.Message);
        }
        
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

        var commitStats = (await _gitHubService.GetCommitStatsAsync(actualLocalPath, null)) // Change sinceDate to null for full history
                                .OrderBy(c => c.CommitDate) // Order by date to ensure LastAnalyzedCommitDate is correct
                                .ToList();
        _logger.LogInformation("Found {CommitCount} new commits to analyze for {RepoName}.", commitStats.Count, repository.Name);

        DateTimeOffset latestCommitDate = repository.LastAnalyzedCommitDate;

        foreach (var commitInfo in commitStats)
        {
            var commitSha = commitInfo.Sha;
            var commitDate = commitInfo.CommitDate;
            var linesAdded = commitInfo.LinesAdded;
            var linesRemoved = commitInfo.LinesRemoved;

            // If LastAnalyzedCommitDate is MinValue, it means we want to re-analyze all commits.
            // Otherwise, only analyze new commits.
            if (repository.LastAnalyzedCommitDate != DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) &&
                await _repositoryDataService.CommitExistsAsync(repository.Id, commitSha))
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
                CommitDate = commitDate, // commitDate is already DateTime
                TotalLines = totalLines,
                LinesAdded = linesAdded,
                LinesRemoved = linesRemoved,
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
            repository.LastAnalyzedCommitDate = latestCommitDate.UtcDateTime; // Use UTC DateTime to avoid Azure Table Storage issues
            await _repositoryDataService.UpdateRepositoryAsync(repository);
        }
        else
        {
            _logger.LogInformation("No new commits processed or latest commit date is not newer for repository ID: {RepositoryId}", repositoryId);
        }

        _logger.LogInformation("Analysis completed for repository ID: {RepositoryId}", repositoryId);
    }

    public async Task UpdateRepositoryAsync(GitHubRepository repository)
    {
        _logger.LogInformation("Updating repository: {Owner}/{Name}", repository.Owner, repository.Name);
        await _repositoryDataService.UpdateRepositoryAsync(repository);
    }

    public async Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId)
    {
        _logger.LogInformation("Retrieving line counts for repository ID: {RepositoryId}", repositoryId);
        return await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repositoryId);
    }

    public async Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days)
    {
        _logger.LogInformation("Retrieving line count history for repository ID: {RepositoryId} for the last {Days} days.", repositoryId, days);

        var repository = await _repositoryDataService.GetRepositoryByIdAsync(repositoryId);
        if (repository == null)
        {
            _logger.LogWarning("Repository with ID {RepositoryId} not found for line count history.", repositoryId);
            return Enumerable.Empty<DailyLineCountDto>();
        }

        var localRepoPath = Path.Combine(repository.Owner, repository.Name); // Relative path within _localReposPath

        // Ensure the repository is cloned/pulled
        try
        {
            await _gitHubService.PullRepositoryAsync(localRepoPath);
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogInformation("Repository not found locally, cloning {RepoUrl} to {LocalPath}", repository.CloneUrl, localRepoPath);
            await _gitHubService.CloneRepositoryAsync(repository.CloneUrl, localRepoPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone or pull repository {RepoName} for line count history. Error: {ErrorMessage}", repository.Name, ex.Message);
            return Enumerable.Empty<DailyLineCountDto>();
        }

        // Get all commit line counts for this repository
        var commitLineCounts = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repositoryId);
        
        if (!commitLineCounts.Any())
        {
            _logger.LogInformation("No commit data found for repository ID: {RepositoryId}", repositoryId);
            return Enumerable.Empty<DailyLineCountDto>();
        }

        // Group commits by date and calculate daily stats
        var dailyStats = new List<DailyLineCountDto>();
        var endDate = DateTime.UtcNow.Date;
        var startDate = endDate.AddDays(-days + 1);

        // Get commits within the date range and group by date
        var commitsInRange = commitLineCounts
            .Where(c => c.CommitDate.Date >= startDate && c.CommitDate.Date <= endDate)
            .GroupBy(c => c.CommitDate.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // If no commits in range, return empty
        if (!commitsInRange.Any())
        {
            _logger.LogInformation("No commits found in the requested date range for repository ID: {RepositoryId}", repositoryId);
            return Enumerable.Empty<DailyLineCountDto>();
        }

        // Calculate cumulative line counts for each day
        var allCommits = commitLineCounts.OrderBy(c => c.CommitDate).ToList();
        
        for (int i = 0; i < days; i++)
        {
            var currentDate = startDate.AddDays(i);
            
            // Get all commits up to this date
            var commitsUpToDate = allCommits.Where(c => c.CommitDate.Date <= currentDate).ToList();
            
            if (commitsUpToDate.Any())
            {
                // Get the latest commit for this date to represent the state at end of day
                var latestCommit = commitsUpToDate.LastOrDefault();
                var commitsOnThisDay = commitsInRange.ContainsKey(currentDate) ? commitsInRange[currentDate] : new List<CommitLineCount>();
                
                var dailyDto = new DailyLineCountDto
                {
                    Date = currentDate,
                    TotalLines = latestCommit?.TotalLines ?? 0,
                    LinesAdded = commitsOnThisDay.Sum(c => c.LinesAdded),
                    LinesRemoved = commitsOnThisDay.Sum(c => c.LinesRemoved),
                    NetLines = commitsOnThisDay.Sum(c => c.LinesAdded - c.LinesRemoved),
                    CommitCount = commitsOnThisDay.Count,
                    LinesByFileType = latestCommit?.LinesByFileType ?? new Dictionary<string, int>()
                };
                
                dailyStats.Add(dailyDto);
            }
            else
            {
                // No commits up to this date, so zero values
                dailyStats.Add(new DailyLineCountDto 
                { 
                    Date = currentDate, 
                    TotalLines = 0,
                    LinesAdded = 0,
                    LinesRemoved = 0,
                    NetLines = 0,
                    CommitCount = 0,
                    LinesByFileType = new Dictionary<string, int>()
                });
            }
        }

        _logger.LogInformation("Returning {Count} daily line count records for repository ID: {RepositoryId}", dailyStats.Count, repositoryId);
        return dailyStats.OrderBy(d => d.Date).ToList();
    }

    public async Task<IEnumerable<RepositoryLineCountHistoryDto>> GetAllRepositoriesLineCountHistoryAsync(int days)
    {
        _logger.LogInformation("Getting line count history for all repositories for the past {Days} days.", days);
        
        var repositories = await GetAllRepositoriesAsync();
        var allRepositoriesHistory = new List<RepositoryLineCountHistoryDto>();

        foreach (var repository in repositories)
        {
            try
            {
                var lineHistory = await GetLineCountHistoryAsync(repository.Id, days);
                var repositoryHistory = new RepositoryLineCountHistoryDto
                {
                    RepositoryId = repository.Id,
                    RepositoryName = repository.Name,
                    Owner = repository.Owner,
                    DailyLineCounts = lineHistory
                };
                allRepositoriesHistory.Add(repositoryHistory);
                _logger.LogDebug("Retrieved {Count} daily records for repository {RepositoryName}", lineHistory.Count(), repository.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving line count history for repository {RepositoryName} (ID: {RepositoryId})", repository.Name, repository.Id);
                // Continue with other repositories even if one fails
            }
        }

        _logger.LogInformation("Returning line count history for {Count} repositories.", allRepositoriesHistory.Count);
        return allRepositoriesHistory;
    }
}
