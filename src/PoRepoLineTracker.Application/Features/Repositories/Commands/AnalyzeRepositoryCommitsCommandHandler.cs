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
        private readonly IFailedOperationService _failedOperationService;
        private readonly IUserService _userService;
        private readonly IUserPreferencesService _userPreferencesService;
        private readonly ILogger<AnalyzeRepositoryCommitsCommandHandler> _logger;

        public AnalyzeRepositoryCommitsCommandHandler(
            IGitHubService gitHubService,
            IRepositoryDataService repositoryDataService,
            IFailedOperationService failedOperationService,
            IUserService userService,
            IUserPreferencesService userPreferencesService,
            ILogger<AnalyzeRepositoryCommitsCommandHandler> logger)
        {
            _gitHubService = gitHubService;
            _repositoryDataService = repositoryDataService;
            _failedOperationService = failedOperationService;
            _userService = userService;
            _userPreferencesService = userPreferencesService;
            _logger = logger;
        }

        public async Task<Unit> Handle(AnalyzeRepositoryCommitsCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Analyzing commits for repository ID: {RepositoryId} (ForceReanalysis: {ForceReanalysis}, ClearExistingData: {ClearExistingData})",
                request.RepositoryId, request.ForceReanalysis, request.ClearExistingData);

            // Get the repository to analyze
            var repository = await _repositoryDataService.GetRepositoryByIdAsync(request.RepositoryId);
            if (repository == null)
            {
                _logger.LogWarning("Repository with ID {RepositoryId} not found", request.RepositoryId);
                return Unit.Value;
            }

            // Clear existing commit data if requested (for full re-analysis with new extensions)
            if (request.ClearExistingData)
            {
                _logger.LogInformation("Clearing existing commit data for repository {RepositoryId} for full re-analysis", request.RepositoryId);
                await _repositoryDataService.DeleteCommitLineCountsForRepositoryAsync(request.RepositoryId);
                
                // Reset the last analyzed date so all commits are processed
                repository.LastAnalyzedCommitDate = null;
                await _repositoryDataService.UpdateRepositoryAsync(repository);
            }

            // Get the user's access token for private repository access
            string? accessToken = null;
            if (repository.UserId != Guid.Empty)
            {
                accessToken = await _userService.GetAccessTokenAsync(repository.UserId);
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
                    await _gitHubService.CloneRepositoryAsync(repository.CloneUrl, localPath, accessToken);
                }
                else
                {
                    localPath = repository.LocalPath;
                    _logger.LogInformation("Pulling repository {Owner}/{Name} from {LocalPath}", repository.Owner, repository.Name, localPath);
                    await _gitHubService.PullRepositoryAsync(localPath, accessToken);
                }

                // Update repository with local path
                repository.LocalPath = localPath;
                await _repositoryDataService.UpdateRepositoryAsync(repository);

                // Get user-specific file extensions to count (falls back to defaults if not configured)
                var fileExtensionsToCount = repository.UserId != Guid.Empty
                    ? await _userPreferencesService.GetFileExtensionsAsync(repository.UserId)
                    : UserPreferences.DefaultFileExtensions;

                // Get commit stats from all time (use a date far in the past to get all commits)
                var sinceDate = DateTime.UtcNow.AddYears(-50); // Get all commits from the repository's entire history
                _logger.LogInformation("Fetching all commit stats for repository {RepositoryId} (since {SinceDate})", request.RepositoryId, sinceDate);
                var commitStats = await _gitHubService.GetCommitStatsAsync(localPath, sinceDate);
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

                        // Record failed operation for retry or analysis
                        var failedOperation = new FailedOperation
                        {
                            Id = Guid.NewGuid(),
                            RepositoryId = request.RepositoryId,
                            OperationType = "CommitProcessing",
                            EntityId = commitStat.Sha,
                            ErrorMessage = ex.Message,
                            StackTrace = ex.StackTrace ?? string.Empty,
                            FailedAt = DateTime.UtcNow,
                            RetryCount = 0,
                            ContextData = new Dictionary<string, object>
                            {
                                { "LocalPath", localPath },
                                { "CommitDate", commitStat.CommitDate },
                                { "LinesAdded", commitStat.LinesAdded },
                                { "LinesRemoved", commitStat.LinesRemoved }
                            }
                        };

                        try
                        {
                            await _failedOperationService.RecordFailedOperationAsync(failedOperation);
                            _logger.LogInformation("Failed commit {CommitSha} recorded in dead letter queue for repository {RepositoryId}",
                                commitStat.Sha, request.RepositoryId);
                        }
                        catch (Exception recordEx)
                        {
                            _logger.LogError(recordEx, "Error recording failed operation for commit {CommitSha} in repository {RepositoryId}",
                                commitStat.Sha, request.RepositoryId);
                        }

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
