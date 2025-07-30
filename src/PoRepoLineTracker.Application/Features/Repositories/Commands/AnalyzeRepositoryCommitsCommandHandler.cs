using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public class AnalyzeRepositoryCommitsCommandHandler : IRequestHandler<AnalyzeRepositoryCommitsCommand, Unit>
    {
        private readonly IGitHubService _gitHubService;
        private readonly IRepositoryDataService _repositoryDataService;
        private readonly ILogger<AnalyzeRepositoryCommitsCommandHandler> _logger;

        public AnalyzeRepositoryCommitsCommandHandler(IGitHubService gitHubService, IRepositoryDataService repositoryDataService, ILogger<AnalyzeRepositoryCommitsCommandHandler> logger)
        {
            _gitHubService = gitHubService;
            _repositoryDataService = repositoryDataService;
            _logger = logger;
        }

        public async Task<Unit> Handle(AnalyzeRepositoryCommitsCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Analyzing commits for repository ID: {RepositoryId} (ForceReanalysis: {ForceReanalysis})", 
                request.RepositoryId, request.ForceReanalysis);
            
            // Get the repository to analyze
            var repository = await _repositoryDataService.GetRepositoryByIdAsync(request.RepositoryId);
            if (repository == null)
            {
                _logger.LogWarning("Repository with ID {RepositoryId} not found", request.RepositoryId);
                return Unit.Value;
            }

            try
            {
                // Clone or pull the repository
                string localPath;
                if (string.IsNullOrEmpty(repository.LocalPath))
                {
                    // Generate a unique local path for this repository
                    localPath = $"repo_{request.RepositoryId}";
                    _logger.LogInformation("Cloning repository {Owner}/{Name} to {LocalPath}", repository.Owner, repository.Name, localPath);
                    await _gitHubService.CloneRepositoryAsync(repository.CloneUrl, localPath);
                }
                else
                {
                    localPath = repository.LocalPath;
                    _logger.LogInformation("Pulling repository {Owner}/{Name} from {LocalPath}", repository.Owner, repository.Name, localPath);
                    await _gitHubService.PullRepositoryAsync(localPath);
                }

                // Update repository with local path
                repository.LocalPath = localPath;
                await _repositoryDataService.UpdateRepositoryAsync(repository);

                // Get configured file extensions to count
                var fileExtensionsToCount = await _repositoryDataService.GetConfiguredFileExtensionsAsync();

                // Get commit stats from the last 365 days (or all commits if less than 365 days of history)
                var commitStats = await _gitHubService.GetCommitStatsAsync(localPath, DateTime.Now.AddDays(-365));
                _logger.LogInformation("Found {CommitCount} commits to analyze for repository {RepositoryId}", commitStats.Count(), request.RepositoryId);

                // Process each commit
                foreach (var commitStat in commitStats)
                {
                    bool shouldProcessCommit = false;
                    CommitLineCount? existingCommit = null;

                    // Check if this commit has already been processed
                    if (await _repositoryDataService.CommitExistsAsync(request.RepositoryId, commitStat.Sha))
                    {
                        if (request.ForceReanalysis)
                        {
                            // Get the existing commit to check if it needs re-analysis
                            var existingCommits = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId);
                            existingCommit = existingCommits.FirstOrDefault(c => c.CommitSha == commitStat.Sha);
                            
                            // Re-process if both LinesAdded and LinesRemoved are zero (indicates old analysis)
                            if (existingCommit != null && existingCommit.LinesAdded == 0 && existingCommit.LinesRemoved == 0)
                            {
                                shouldProcessCommit = true;
                                _logger.LogDebug("Force re-analyzing commit {CommitSha} with missing diff data", commitStat.Sha);
                            }
                            else
                            {
                                _logger.LogDebug("Commit {CommitSha} already has diff data, skipping", commitStat.Sha);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Commit {CommitSha} already processed, skipping", commitStat.Sha);
                        }
                    }
                    else
                    {
                        // New commit, always process
                        shouldProcessCommit = true;
                    }

                    if (!shouldProcessCommit)
                    {
                        continue;
                    }

                    try
                    {
                        // Count lines in this commit by file type
                        var lineCounts = await _gitHubService.CountLinesInCommitAsync(localPath, commitStat.Sha, fileExtensionsToCount);
                        var totalLines = lineCounts.Values.Sum();

                        // Create and store commit line count record with diff stats
                        var commitLineCount = new CommitLineCount
                        {
                            RepositoryId = request.RepositoryId,
                            CommitSha = commitStat.Sha,
                            CommitDate = commitStat.CommitDate,
                            TotalLines = totalLines,
                            LinesAdded = commitStat.LinesAdded,     // Now properly setting lines added from diff
                            LinesRemoved = commitStat.LinesRemoved, // Now properly setting lines removed from diff
                            LinesByFileType = lineCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                        };

                        await _repositoryDataService.AddCommitLineCountAsync(commitLineCount);
                        _logger.LogDebug("Processed commit {CommitSha} with {TotalLines} lines (Added: {LinesAdded}, Removed: {LinesRemoved})", 
                            commitStat.Sha, totalLines, commitStat.LinesAdded, commitStat.LinesRemoved);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing commit {CommitSha} for repository {RepositoryId}", commitStat.Sha, request.RepositoryId);
                        // Continue with other commits even if one fails
                    }
                }

                _logger.LogInformation("Completed analysis for repository ID: {RepositoryId}", request.RepositoryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing repository {RepositoryId}", request.RepositoryId);
                throw; // Re-throw to let the API handle the error
            }
            
            return Unit.Value;
        }
    }
}
