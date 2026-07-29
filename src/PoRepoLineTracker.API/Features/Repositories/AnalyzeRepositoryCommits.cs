using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using static PoRepoLineTracker.API.Features.Repositories.CommitTaggerService;

namespace PoRepoLineTracker.API.Features.Repositories;

/// <summary>
/// Command to analyze commits for a repository.
/// </summary>
/// <param name="RepositoryId">The repository to analyze</param>
/// <param name="ForceReanalysis">If true, re-analyze commits that have missing diff data</param>
/// <param name="ClearExistingData">If true, delete all existing commit data and re-analyze from scratch</param>
public record AnalyzeRepositoryCommitsCommand(
    RepositoryId RepositoryId,
    bool ForceReanalysis = false,
    bool ClearExistingData = false) : IRequest<Unit>;

public class AnalyzeRepositoryCommitsCommandHandler : IRequestHandler<AnalyzeRepositoryCommitsCommand, Unit>
{
    private readonly IGitHubService _gitHubService;
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly IUserService _userService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IAnalysisProgressService _progressService;
    private readonly IAiDetectionService _aiDetectionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnalyzeRepositoryCommitsCommandHandler> _logger;

    // #10 fix: per-repository semaphore prevents git Checkout() race conditions on shared local path
    private static readonly ConcurrentDictionary<RepositoryId, SemaphoreSlim> _repoLocks = new();

    public AnalyzeRepositoryCommitsCommandHandler(
        IGitHubService gitHubService,
        IRepositoryDataService repositoryDataService,
        IUserService userService,
        IUserPreferencesService userPreferencesService,
        IAnalysisProgressService progressService,
        IAiDetectionService aiDetectionService,
        IConfiguration configuration,
        ILogger<AnalyzeRepositoryCommitsCommandHandler> logger)
    {
        _gitHubService = gitHubService;
        _repositoryDataService = repositoryDataService;
        _userService = userService;
        _userPreferencesService = userPreferencesService;
        _progressService = progressService;
        _aiDetectionService = aiDetectionService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Unit> Handle(AnalyzeRepositoryCommitsCommand request, CancellationToken cancellationToken)
    {
        // #10 fix: if another analysis is already running for this repo, skip instead of racing
        var semaphore = _repoLocks.GetOrAdd(request.RepositoryId, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            _logger.LogWarning("Analysis for repository {RepositoryId} already in progress — skipping concurrent request", request.RepositoryId);
            return Unit.Value;
        }

        try
        {
            return await HandleInternalAsync(request, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Unit> HandleInternalAsync(AnalyzeRepositoryCommitsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Analyzing commits for repository ID: {RepositoryId} (ForceReanalysis: {ForceReanalysis}, ClearExistingData: {ClearExistingData})",
            request.RepositoryId, request.ForceReanalysis, request.ClearExistingData);

        // Get the repository to analyze
        var repository = await _repositoryDataService.GetRepositoryByIdAsync(request.RepositoryId);
        if (repository == null)
        {
            _logger.LogWarning("Repository with ID {RepositoryId} not found", request.RepositoryId);
            _progressService.ReportError(request.RepositoryId, "Repository not found.");
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

        // Resolve a GitHub-compatible access token for the clone/pull.
        // IMPORTANT: a Microsoft Graph OAuth access token is a JWT (~1.5KB, contains
        // '+' and '/' segments) and is NOT a GitHub token. Stuffing it into the clone
        // URL as userinfo causes libcurl to reject it with "Port number was not a
        // decimal number" because it parses parts of the JWT as a port. So we only
        // use the user's stored token when the user actually signed in with GitHub;
        // for Microsoft-authenticated users (or users with a missing/empty token)
        // we fall back to the server-configured GitHub:PAT.
        string? accessToken = null;
        if (repository.UserId != UserId.Empty)
        {
            var user = await _userService.GetUserByIdAsync(repository.UserId);
            var loggedInWithGitHub = user is not null
                && !string.IsNullOrEmpty(user.GitHubId)
                && !user.GitHubId.StartsWith("ms:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(user.AccessToken);

            if (loggedInWithGitHub)
            {
                accessToken = user!.AccessToken;
            }
            else
            {
                // Microsoft-authenticated user — fall back to the server-side GitHub PAT.
                // GitHubEndpoints.cs applies the same rule for /api/github/user-repositories.
                var configuredPat = _configuration[ConfigKeys.GitHub.Pat];
                if (!string.IsNullOrEmpty(configuredPat))
                {
                    _logger.LogInformation(
                        "User {UserId} signed in with Microsoft; using server-configured GitHub:PAT for clone of {Owner}/{Name}",
                        repository.UserId, repository.Owner, repository.Name);
                    accessToken = configuredPat;
                }
            }
        }
        else
        {
            // Repository not tied to a user (e.g. legacy data) — try the server PAT.
            var configuredPat = _configuration[ConfigKeys.GitHub.Pat];
            if (!string.IsNullOrEmpty(configuredPat))
            {
                accessToken = configuredPat;
            }
        }

        try
        {
            // Determine if this is a locally uploaded repository
            bool isLocalUpload = string.IsNullOrWhiteSpace(repository.CloneUrl);

            // ── Step 1: Clone/pull OR validate local repository ───────────────────────
            _progressService.ReportStep(request.RepositoryId, 1, "Cloning",
                isLocalUpload
                    ? $"Step 1/4 — Validating local repository {repository.Owner}/{repository.Name}"
                    : $"Step 1/4 — Cloning/pulling {repository.Owner}/{repository.Name}");
            _logger.LogInformation("[Step 1/4] {Status} for repository {RepositoryId}",
                isLocalUpload ? "Validating local repo" : "Clone/pull", request.RepositoryId);

            string localPath;
            string fullRepoPath;

            if (isLocalUpload)
            {
                // For locally uploaded repos, the LocalPath contains the full path to the .git folder's parent
                fullRepoPath = repository.LocalPath;

                // Validate the local repository
                bool isValid = await _gitHubService.IsLocalRepositoryValidAsync(fullRepoPath);
                if (!isValid)
                {
                    _logger.LogError("Local repository at {FullPath} is not valid or does not exist", fullRepoPath);
                    _progressService.ReportError(request.RepositoryId, "Local repository is not valid or does not exist.");
                    return Unit.Value;
                }

                _logger.LogInformation("Local repository validated at {FullPath}", fullRepoPath);
                localPath = fullRepoPath; // Use full path for local repos
            }
            else
            {
                // Standard GitHub repository path handling
                // Always derive a stable local path from the repo ID so we can re-clone safely
                // after an Azure App Service container restart (ephemeral filesystem).
                localPath = string.IsNullOrEmpty(repository.LocalPath)
                    ? $"repo_{request.RepositoryId}"
                    : repository.LocalPath;

                bool repoExistsLocally = await _gitHubService.IsRepositoryValidAsync(localPath);
                if (repoExistsLocally)
                {
                    _logger.LogInformation("Pulling repository {Owner}/{Name} from {LocalPath}", repository.Owner, repository.Name, localPath);
                    try
                    {
                        await _gitHubService.PullRepositoryAsync(localPath, accessToken);
                    }
                    catch (Exception pullEx)
                    {
                        _logger.LogWarning(pullEx,
                            "Pull failed for repository {RepositoryId} at {LocalPath} — deleting local copy and re-cloning",
                            request.RepositoryId, localPath);
                        await _gitHubService.DeleteLocalRepositoryAsync(localPath);
                        await _gitHubService.CloneRepositoryAsync(repository.CloneUrl, localPath, accessToken);
                    }
                }
                else
                {
                    _logger.LogInformation("Local path missing or invalid — cloning repository {Owner}/{Name} to {LocalPath}", repository.Owner, repository.Name, localPath);
                    await _gitHubService.CloneRepositoryAsync(repository.CloneUrl, localPath, accessToken);
                }

                // Update repository with local path
                repository.LocalPath = localPath;
                await _repositoryDataService.UpdateRepositoryAsync(repository);

                // Get the full path for later use
                var homePath = Environment.GetEnvironmentVariable("HOME");
                fullRepoPath = !string.IsNullOrEmpty(homePath)
                    ? Path.Combine(homePath, "site", "wwwroot", "temp_repos", localPath)
                    : Path.Combine(Directory.GetCurrentDirectory(), "LocalRepos", localPath);
            }

            // Get user-specific file extensions to count (falls back to defaults if not configured)
            var fileExtensionsToCount = repository.UserId != UserId.Empty
                ? await _userPreferencesService.GetFileExtensionsAsync(repository.UserId)
                : UserPreferences.DefaultFileExtensions;

            // ── Step 2: Fetch all commit stats ────────────────────────────────────────
            _progressService.ReportStep(request.RepositoryId, 2, "Fetching",
                $"Step 2/4 — Fetching commit history for {repository.Owner}/{repository.Name}");
            _logger.LogInformation("[Step 2/4] Fetching commit stats for repository {RepositoryId}", request.RepositoryId);

            // Get commit stats from all time (use a date far in the past to get all commits)
            var sinceDate = DateTime.UtcNow.AddYears(-50); // Get all commits from the repository's entire history
            _logger.LogInformation("Fetching all commit stats for repository {RepositoryId} (since {SinceDate})", request.RepositoryId, sinceDate);

            IEnumerable<CommitStatsDto> commitStats;
            if (isLocalUpload)
            {
                commitStats = await _gitHubService.GetCommitStatsFromFullPathAsync(fullRepoPath, sinceDate);
            }
            else
            {
                commitStats = await _gitHubService.GetCommitStatsAsync(localPath, sinceDate);
            }

            var commitStatsList = commitStats.ToList();
            _logger.LogInformation("Found {CommitCount} commits to analyze for repository {RepositoryId}", commitStatsList.Count, request.RepositoryId);
            _progressService.ReportCommitsFound(request.RepositoryId, commitStatsList.Count);

            // #3 fix: pre-load ALL existing commits in one query so the loop never re-fetches per-SHA
            Dictionary<string, CommitLineCount>? existingCommitsBySha = null;
            if (request.ForceReanalysis)
            {
                _logger.LogDebug("Pre-loading existing commits for ForceReanalysis on repository {RepositoryId}", request.RepositoryId);
                var allExisting = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId);
                existingCommitsBySha = allExisting.ToDictionary(c => c.CommitSha);
                _logger.LogDebug("Pre-loaded {Count} existing commits for repository {RepositoryId}", existingCommitsBySha.Count, request.RepositoryId);
            }

            // ── Step 3: Process each commit ───────────────────────────────────────────
            _progressService.ReportStep(request.RepositoryId, 3, "Processing",
                $"Step 3/4 — Processing {commitStatsList.Count} commits");
            _logger.LogInformation("[Step 3/4] Processing commits for repository {RepositoryId}", request.RepositoryId);

            int processedCount = 0;
            // Process each commit
            foreach (var commitStat in commitStatsList)
            {
                bool shouldProcessCommit = false;
                CommitLineCount? existingCommit = null;

                // Check if this commit has already been processed
                if (await _repositoryDataService.CommitExistsAsync(request.RepositoryId, commitStat.Sha))
                {
                    if (request.ForceReanalysis)
                    {
                        // #3 fix: look up from pre-loaded dictionary — no extra Azure Table query per commit
                        existingCommitsBySha!.TryGetValue(commitStat.Sha, out existingCommit);

                        // Re-process if both LinesAdded and LinesRemoved are zero (indicates old analysis)
                        if (existingCommit != null && existingCommit.LinesAdded == 0 && existingCommit.LinesRemoved == 0)
                        {
                            shouldProcessCommit = true;
                            _logger.ForceReanalyzingCommit(commitStat.Sha);
                        }
                        else
                        {
                            _logger.CommitAlreadyHasDiff(commitStat.Sha);
                        }
                    }
                    else
                    {
                        _logger.CommitAlreadyProcessed(commitStat.Sha);
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
                    Dictionary<string, int> lineCounts;
                    if (isLocalUpload)
                    {
                        lineCounts = await _gitHubService.CountLinesInCommitFromFullPathAsync(fullRepoPath, commitStat.Sha, fileExtensionsToCount);
                    }
                    else
                    {
                        lineCounts = await _gitHubService.CountLinesInCommitAsync(localPath, commitStat.Sha, fileExtensionsToCount);
                    }
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
                        LinesByFileType = lineCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        AuthorName = commitStat.AuthorName,
                        AuthorEmail = commitStat.AuthorEmail
                    };

                    // CommitTagger: classify the commit with algorithmic tags
                    commitLineCount.Tags = ClassifyCommit(commitLineCount);

                    await _repositoryDataService.AddCommitLineCountAsync(commitLineCount);
                    _logger.ProcessedCommit(commitStat.Sha, totalLines, commitStat.LinesAdded, commitStat.LinesRemoved);

                    // Report commit progress every 5 commits to avoid excessive updates
                    processedCount++;
                    if (processedCount % 5 == 0 || processedCount == commitStatsList.Count)
                    {
                        _progressService.ReportCommitProgress(request.RepositoryId, processedCount, commitStatsList.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing commit {CommitSha} for repository {RepositoryId}", commitStat.Sha, request.RepositoryId);

                    // Surface the failure via telemetry (Serilog error above + OpenTelemetry counter).
                    AppTelemetry.FailedOperations.Add(1,
                        new KeyValuePair<string, object?>("operation", "CommitProcessing"),
                        new KeyValuePair<string, object?>("repository.id", request.RepositoryId));

                    // Continue with other commits even if one fails
                }
            }

            // ── Step 4: Calculate top files ───────────────────────────────────────────
            _progressService.ReportStep(request.RepositoryId, 4, "Saving",
                $"Step 4/4 — Calculating top files for {repository.Owner}/{repository.Name}");
            _logger.LogInformation("[Step 4/4] Calculating top files for repository ID: {RepositoryId}", request.RepositoryId);
            // After processing all commits, calculate and store top files
            _logger.LogInformation("Calculating top files for repository ID: {RepositoryId}", request.RepositoryId);
            try
            {
                IEnumerable<TopFileDto> topFiles;
                if (isLocalUpload)
                {
                    topFiles = await _gitHubService.GetTopFilesByLineCountFromFullPathAsync(fullRepoPath, fileExtensionsToCount, 100);
                }
                else
                {
                    topFiles = await _gitHubService.GetTopFilesByLineCountAsync(localPath, fileExtensionsToCount, 100);
                }
                await _repositoryDataService.SaveTopFilesAsync(request.RepositoryId, topFiles);
                _logger.LogInformation("Saved top files for repository ID: {RepositoryId}", request.RepositoryId);
            }
            catch (Exception topFilesEx)
            {
                _logger.LogError(topFilesEx, "Error calculating/saving top files for repository {RepositoryId}", request.RepositoryId);
                // Don't fail the whole analysis if top files calculation fails
            }

            // Update LastAnalyzedCommitDate to the latest commit date so the UI shows "Analyzed"
            if (commitStatsList.Any())
            {
                var latestCommitDate = commitStatsList.Max(c => c.CommitDate);
                repository.LastAnalyzedCommitDate = latestCommitDate;
                await _repositoryDataService.UpdateRepositoryAsync(repository);
                _logger.LogInformation("Updated LastAnalyzedCommitDate to {Date} for repository {RepositoryId}", latestCommitDate, request.RepositoryId);
            }

            _progressService.ReportComplete(request.RepositoryId);
            _logger.LogInformation("Completed analysis for repository ID: {RepositoryId}", request.RepositoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing repository {RepositoryId}", request.RepositoryId);
            _progressService.ReportError(request.RepositoryId, ex.Message);
            throw; // Re-throw to let the API handle the error
        }

        return Unit.Value;
    }
}
