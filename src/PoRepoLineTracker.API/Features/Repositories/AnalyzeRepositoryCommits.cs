using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;

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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnalyzeRepositoryCommitsCommandHandler> _logger;

    // #10 fix: per-repository semaphore prevents git Checkout() race conditions on shared local path
    private static readonly ConcurrentDictionary<RepositoryId, SemaphoreSlim> _repoLocks = new();

    /// <summary>
    /// How many file types the live view is told about. It renders them as a row of chips that
    /// light up as each is discovered; past a dozen the row wraps into a wall and the tail is
    /// all single-file extensions nobody is watching for.
    /// </summary>
    private const int ReportedExtensionCount = 12;

    public AnalyzeRepositoryCommitsCommandHandler(
        IGitHubService gitHubService,
        IRepositoryDataService repositoryDataService,
        IUserService userService,
        IUserPreferencesService userPreferencesService,
        IAnalysisProgressService progressService,
        IConfiguration configuration,
        ILogger<AnalyzeRepositoryCommitsCommandHandler> logger)
    {
        _gitHubService = gitHubService;
        _repositoryDataService = repositoryDataService;
        _userService = userService;
        _userPreferencesService = userPreferencesService;
        _progressService = progressService;
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

        // Opens the job and records its owner before any step is reported — the progress service
        // pushes each update to that user's connections and drops any report for a job whose
        // owner it does not know. Placed here rather than at the two endpoints that queue the
        // command because this is where the repository (and so its UserId) is actually loaded.
        _progressService.BeginJob(request.RepositoryId, repository.UserId, repository.Owner, repository.Name);

        // Clear existing commit data if requested (for full re-analysis with new extensions)
        if (request.ClearExistingData)
        {
            _logger.LogInformation("Clearing existing commit data for repository {RepositoryId} for full re-analysis", request.RepositoryId);
            await _repositoryDataService.DeleteCommitLineCountsForRepositoryAsync(request.RepositoryId);

            // Reset the last analyzed date so all commits are processed
            repository.LastAnalyzedCommitDate = null;
            await _repositoryDataService.UpdateRepositoryAsync(repository);
        }

        // Resolve the GitHub access token for the clone/pull. GitHub OAuth is the only
        // provider, so a stored token is always a GitHub token; the server-configured
        // GitHub:PAT covers rows with a missing/empty token and user-less legacy rows.
        string? accessToken = null;
        if (repository.UserId != UserId.Empty)
        {
            var user = await _userService.GetUserByIdAsync(repository.UserId);
            if (!string.IsNullOrEmpty(user?.AccessToken))
            {
                accessToken = user.AccessToken;
            }
        }

        if (string.IsNullOrEmpty(accessToken))
        {
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

            // The ONE path variable from here on. An uploaded repository stores an absolute
            // LocalPath and a cloned one stores a relative path, and ResolveRepositoryPath is what
            // reconciles the two — so every read below takes the same argument regardless of where
            // the repository came from. This used to be a pair of variables (`localPath` and
            // `fullRepoPath`), one of which was assigned a meaningless value on each branch, with
            // every subsequent call re-testing isLocalUpload to decide which to pass.
            string repositoryPath;

            if (isLocalUpload)
            {
                repositoryPath = _gitHubService.ResolveRepositoryPath(repository.LocalPath);

                if (!await _gitHubService.IsRepositoryValidAsync(repositoryPath))
                {
                    _logger.LogError("Local repository at {RepoPath} is not valid or does not exist", repositoryPath);
                    _progressService.ReportError(request.RepositoryId, "Local repository is not valid or does not exist.");
                    return Unit.Value;
                }

                _logger.LogInformation("Local repository validated at {RepoPath}", repositoryPath);
            }
            else
            {
                // Always derive a stable local path from the repo ID so we can re-clone safely
                // after an Azure App Service container restart (ephemeral filesystem).
                var localPath = string.IsNullOrEmpty(repository.LocalPath)
                    ? $"repo_{request.RepositoryId}"
                    : repository.LocalPath;

                repositoryPath = _gitHubService.ResolveRepositoryPath(localPath);

                if (await _gitHubService.IsRepositoryValidAsync(repositoryPath))
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

                // Clone and pull still take the RELATIVE path: they own the base-directory
                // convention, and that relative form is what is persisted on the row.
                repository.LocalPath = localPath;
                await _repositoryDataService.UpdateRepositoryAsync(repository);
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

            var commitStatsList = (await _gitHubService.GetCommitStatsAsync(repositoryPath, sinceDate)).ToList();
            _logger.LogInformation("Found {CommitCount} commits to analyze for repository {RepositoryId}", commitStatsList.Count, request.RepositoryId);
            _progressService.ReportCommitsFound(request.RepositoryId, commitStatsList.Count);

            // Pre-load ALL existing commits in ONE query, unconditionally.
            //
            // This used to be gated on ForceReanalysis, which meant the ordinary incremental path
            // — by far the common one — still asked storage "does this SHA exist?" once per
            // commit inside the loop below. On a repository with a few thousand commits that is a
            // few thousand Azure Table round-trips (each one also emitting two Information-level
            // log lines) to answer a question one query already had the answer to, and it was the
            // dominant cost of re-analysing an up-to-date repository: almost every commit is
            // already stored, so almost every iteration paid for a round-trip and then did
            // nothing.
            //
            // The whole set is what a single query returns anyway, so the gate saved nothing even
            // when it applied.
            _logger.LogDebug("Pre-loading existing commits for repository {RepositoryId}", request.RepositoryId);
            var existingCommitsBySha = (await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId))
                // Duplicate SHAs are not supposed to exist — the SHA is the row key — but a
                // ToDictionary that throws here would abort the whole analysis over a storage
                // anomaly this loop is perfectly able to tolerate.
                .GroupBy(c => c.CommitSha, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _logger.LogDebug("Pre-loaded {Count} existing commits for repository {RepositoryId}", existingCommitsBySha.Count, request.RepositoryId);

            // ── Step 3: Process each commit ───────────────────────────────────────────
            _progressService.ReportStep(request.RepositoryId, 3, "Processing",
                $"Step 3/4 — Processing {commitStatsList.Count} commits");
            _logger.LogInformation("[Step 3/4] Processing commits for repository {RepositoryId}", request.RepositoryId);

            int processedCount = 0;

            // Live-view tallies, reported alongside the commit counts. Kept here rather than
            // recomputed by the progress service because this loop is the only place the
            // per-commit breakdown exists — the service holds one snapshot, not a history.
            long linesCountedSoFar = 0;
            var extensionTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            // Process each commit
            foreach (var commitStat in commitStatsList)
            {
                bool shouldProcessCommit = false;

                // Answered from the pre-loaded set — no per-commit round-trip. See the pre-load above.
                if (existingCommitsBySha.TryGetValue(commitStat.Sha, out var existingCommit))
                {
                    if (request.ForceReanalysis)
                    {
                        // Re-process if both LinesAdded and LinesRemoved are zero (indicates old analysis)
                        if (existingCommit.LinesAdded == 0 && existingCommit.LinesRemoved == 0)
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
                    var lineCounts = await _gitHubService.CountLinesInCommitAsync(repositoryPath, commitStat.Sha, fileExtensionsToCount);
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


                    linesCountedSoFar += totalLines;
                    foreach (var (extension, lines) in lineCounts)
                        extensionTotals[extension] = extensionTotals.GetValueOrDefault(extension) + lines;

                    await _repositoryDataService.AddCommitLineCountAsync(commitLineCount);
                    _logger.ProcessedCommit(commitStat.Sha, totalLines, commitStat.LinesAdded, commitStat.LinesRemoved);

                    // Report commit progress every 5 commits to avoid excessive updates
                    processedCount++;
                    if (processedCount % 5 == 0 || processedCount == commitStatsList.Count)
                    {
                        // A fresh list each time: the progress DTO is serialized on a
                        // background task, so handing it a collection this loop keeps mutating
                        // would throw mid-send.
                        _progressService.ReportCommitProgress(
                            request.RepositoryId,
                            processedCount,
                            commitStatsList.Count,
                            linesCountedSoFar,
                            extensionTotals
                                .OrderByDescending(kv => kv.Value)
                                .Take(ReportedExtensionCount)
                                .Select(kv => kv.Key)
                                .ToList());
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

            // ── Step 4: Save results ──────────────────────────────────────────────────
            _progressService.ReportStep(request.RepositoryId, 4, "Saving",
                $"Step 4/4 — Saving results for {repository.Owner}/{repository.Name}");

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
