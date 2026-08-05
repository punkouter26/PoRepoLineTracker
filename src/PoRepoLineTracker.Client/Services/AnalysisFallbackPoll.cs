using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Shared.Domain;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Shared.Serialization;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// The fallback for when the SignalR hub cannot be reached — a proxy that blocks WebSockets, or a
/// failed handshake. It polls the repositories it has been asked to watch and turns whatever the
/// server says into the SAME <see cref="AnalysisProgressDto"/> frames the hub pushes, handing them
/// to the <c>publish</c> delegate it was constructed with.
///
/// <para><b>It decides nothing.</b> There is one completion path in this application and it lives
/// in the page's frame handler; this class exists to feed that handler by a second route, not to
/// be a second route. Do not add reload/notify/clear-badge logic here — the previous arrangement
/// had three such copies (a 15-second poll, a 5-second per-reanalysis poll, and the hub handler)
/// and they had already drifted apart on which state each one cleaned up.</para>
///
/// <para>Extracted verbatim out of Repositories.razor. Same loop, same tick, same synthesised
/// frames — the page now injects its dependencies rather than closing over its own fields.</para>
/// </summary>
internal sealed class AnalysisFallbackPoll
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly Func<bool> _isFeedConnected;
    private readonly Func<AnalysisProgressDto, Task> _publish;
    private readonly Action<GitHubRepository> _onRepositoryRefreshed;
    private readonly Func<IReadOnlyDictionary<RepositoryId, AnalysisProgressDto>> _seenProgress;

    /// <summary>
    /// Repositories being waited on, each with the LastAnalyzedCommitDate it had when the wait
    /// began — that value changing is the completion signal.
    /// </summary>
    private readonly Dictionary<RepositoryId, DateTime?> _awaiting = [];

    private bool _running;

    public AnalysisFallbackPoll(
        HttpClient http,
        ILogger logger,
        Func<bool> isFeedConnected,
        Func<AnalysisProgressDto, Task> publish,
        Action<GitHubRepository> onRepositoryRefreshed,
        Func<IReadOnlyDictionary<RepositoryId, AnalysisProgressDto>> seenProgress)
    {
        _http = http;
        _logger = logger;
        _isFeedConnected = isFeedConnected;
        _publish = publish;
        _onRepositoryRefreshed = onRepositoryRefreshed;
        _seenProgress = seenProgress;
    }

    /// <summary>Registers a repository to watch, with the date it had when the wait began.</summary>
    public void Watch(RepositoryId repositoryId, DateTime? baseline) => _awaiting[repositoryId] = baseline;

    /// <summary>Stops waiting on a repository — called when a hub frame settles it first.</summary>
    public void StopWatching(RepositoryId repositoryId) => _awaiting.Remove(repositoryId);

    /// <summary>
    /// Starts the loop if anything is being waited on and the hub is not carrying it.
    ///
    /// <para>The hub reports every step of every job the user owns, so polling on top of it would
    /// fetch the same data twice.</para>
    ///
    /// <para>Takes the <see cref="CancellationTokenSource"/>, not its Token, and reads
    /// <c>.Token</c> only on the path that actually starts a loop. Reading it here would throw
    /// <see cref="ObjectDisposedException"/> whenever the caller reaches this method after its own
    /// disposal — which the page does routinely: navigating away disposes the source while the
    /// awaited loads inside OnInitializedAsync are still in flight, and the continuation then
    /// calls straight into here. In the common case the hub IS connected, so the guards below
    /// return before the token is ever needed.</para>
    /// </summary>
    public void StartIfNeeded(IEnumerable<GitHubRepository> repositories, CancellationTokenSource disposeCts)
    {
        foreach (var pending in repositories.Where(r => r.LastAnalyzedCommitDate is null))
            _awaiting.TryAdd(pending.Id, null);

        if (_running) return;
        if (_isFeedConnected()) return;
        if (_awaiting.Count == 0) return;

        _ = RunAsync(disposeCts);
    }

    private async Task RunAsync(CancellationTokenSource disposeCts)
    {
        _running = true;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token);
        // Hard stop. A very large repository must not leave a timer running for the whole session.
        cts.CancelAfter(TimeSpan.FromMinutes(60));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _logger.LogInformation("Fallback analysis poll started for {Count} repositories", _awaiting.Count);

        try
        {
            while (_awaiting.Count > 0 && await timer.WaitForNextTickAsync(cts.Token))
            {
                // The hub may come back mid-job — a reconnect is exactly what WithAutomaticReconnect
                // is for. Stand down when it does rather than duplicating its frames.
                if (_isFeedConnected())
                {
                    _logger.LogInformation("Analysis feed reconnected — stopping fallback poll");
                    break;
                }

                try
                {
                    await TickAsync(cts.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fallback poll tick failed — will retry");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Fallback analysis poll cancelled");
        }
        finally
        {
            _running = false;
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var repos = await _http.GetAppJsonAsync("/api/repositories", AppJsonSerializerContext.Default.ListGitHubRepository, cancellationToken);
        if (repos is null) return;

        foreach (var (repositoryId, baseline) in _awaiting.ToList())
        {
            var updated = repos.FirstOrDefault(r => r.Id == repositoryId);

            // Deleted from under us — stop waiting rather than polling a 404 for an hour.
            if (updated is null)
            {
                _awaiting.Remove(repositoryId);
                continue;
            }

            // A changed LastAnalyzedCommitDate is the completion signal. Checked BEFORE the
            // progress endpoint because a finished job's progress entry expires, and reading
            // "no progress" as "still working" is what used to leave badges spinning.
            if (updated.LastAnalyzedCommitDate is not null && updated.LastAnalyzedCommitDate != baseline)
            {
                _onRepositoryRefreshed(updated);
                await PublishTerminalFrameAsync(updated, error: null);
                continue;
            }

            // Still in flight. Mirror the running frame the hub would have pushed, so the step
            // name, commit counts and stuck/error flags reach the grid by the same route.
            AnalysisProgressDto? progress = null;
            try
            {
                progress = await _http.GetAppJsonAsync(
                    $"/api/repositories/{repositoryId}/analysis-progress",
                    AppJsonSerializerContext.Default.AnalysisProgressDto,
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 404 until the job registers itself. Not an error — just nothing to report yet.
            }

            if (progress is null) continue;

            if (progress.IsRunning || progress.ErrorMessage is not null)
            {
                await _publish(progress);
                if (!progress.IsRunning) _awaiting.Remove(repositoryId);
            }

            // A terminal frame with no error and no date change yet means the write has not
            // landed. Leave it outstanding and pick the completion up on a later tick.
        }
    }

    /// <summary>
    /// Synthesises the terminal frame the hub would have pushed and routes it through the one
    /// handler, so completion does the same work by either path.
    /// </summary>
    private async Task PublishTerminalFrameAsync(GitHubRepository repo, string? error)
    {
        _awaiting.Remove(repo.Id);

        // The handler only acts on a running → finished transition, so a repository never seen
        // running needs its running frame first. Without this, the completion of a job the poll
        // only ever caught at the end would be silently dropped.
        if (!(_seenProgress().TryGetValue(repo.Id, out var seen) && seen.IsRunning))
        {
            await _publish(new AnalysisProgressDto
            {
                RepositoryId = repo.Id,
                Owner = repo.Owner,
                Name = repo.Name,
                IsRunning = true,
                StepName = "Analyzing",
                StepDescription = "Working…"
            });
        }

        await _publish(new AnalysisProgressDto
        {
            RepositoryId = repo.Id,
            Owner = repo.Owner,
            Name = repo.Name,
            IsRunning = false,
            StepName = error is null ? "Complete" : "Failed",
            StepDescription = error ?? "Analysis complete",
            ErrorMessage = error
        });
    }
}
