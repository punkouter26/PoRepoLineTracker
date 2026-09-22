using PoRepoLineTracker.API.Storage;

namespace PoRepoLineTracker.API.Features.Repositories;

/// <summary>
/// Resumes analyses that a restart interrupted.
///
/// <para>The bulk-add endpoint queues its per-repository analysis loop with fire-and-forget
/// <c>Task.Run</c>, which dies with the process. A restart mid-import therefore left the
/// remaining repositories stuck "PENDING" forever — their rows existed with
/// <see cref="GitHubRepository.LastAnalyzedCommitDate"/> null and nothing ever looked at them
/// again. This hosted service sweeps those rows once at startup and drains them through the
/// same handler the endpoint uses, so a killed analysis finishes on the next launch.</para>
/// </summary>
public sealed class ResumePendingAnalysis(
    IServiceScopeFactory scopeFactory,
    ILogger<ResumePendingAnalysis> logger) : BackgroundService
{
    // Hosted services start before the storage client has proven its table exists; the analysis
    // handler's first query would otherwise race app warm-up. Short, and only on the startup path.
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupGrace, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        List<GitHubRepository> pending;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IRepositoryDataService>();
            pending = (await dataService.GetUnanalyzedRepositoriesAsync()).ToList();
        }
        catch (Exception ex)
        {
            // Storage not reachable at startup (emulator down, table create failed). Failing the
            // whole app over a resume sweep is the wrong trade — the bulk-add path still works.
            logger.LogError(ex, "Resume sweep could not read pending repositories — skipping this startup");
            return;
        }

        if (pending.Count == 0) return;
        logger.LogInformation("Resume sweep: {Count} repositor{Y} never analyzed — queueing now",
            pending.Count, pending.Count == 1 ? "y" : "ies");

        using var workerScope = scopeFactory.CreateScope();
        var analyzer = workerScope.ServiceProvider.GetRequiredService<AnalyzeRepositoryCommitsCommandHandler>();

        foreach (var repo in pending)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                // Shutdown mid-sweep: whatever is still null is picked up on the next start.
                logger.LogInformation("Resume sweep stopped early — remaining repositories retry on next startup");
                return;
            }

            try
            {
                logger.LogInformation("Resume: starting analysis for {Owner}/{Name} ({RepoId})", repo.Owner, repo.Name, repo.Id);
                await analyzer.Handle(new AnalyzeRepositoryCommitsCommand(repo.Id), stoppingToken);
                logger.LogInformation("Resume: analysis complete for {Owner}/{Name}", repo.Owner, repo.Name);
            }
            catch (Exception ex)
            {
                // One broken repository must not stop the rest of the sweep.
                logger.LogError(ex, "Resume: analysis failed for {Owner}/{Name}: {Message}", repo.Owner, repo.Name, ex.Message);
            }
        }
    }
}
