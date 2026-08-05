using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using PoRepoLineTracker.API.Hubs;

namespace PoRepoLineTracker.API.Features.Repositories;

/// <summary>
/// In-memory singleton that stores live analysis progress for background jobs.
/// Safe for concurrent access from multiple background Task.Run threads.
///
/// <para>Every mutation is also pushed to the owning user's SignalR group, so the UI reflects a
/// step change immediately instead of on the next poll. <see cref="IHubContext{THub}"/> is
/// registered as a singleton by <c>AddSignalR</c>, so taking it on a singleton is safe — unlike a
/// scoped service, which is why ownership is captured up front in <see cref="BeginJob"/> rather
/// than resolved here.</para>
/// </summary>
public sealed class AnalysisProgressService(
    IHubContext<AnalysisHub> hubContext,
    ILogger<AnalysisProgressService> logger) : IAnalysisProgressService
{
    private readonly ConcurrentDictionary<RepositoryId, AnalysisProgressDto> _progress = new();

    /// <summary>Owner of each tracked job — the address a push is sent to.</summary>
    private readonly ConcurrentDictionary<RepositoryId, UserId> _jobOwners = new();

    public void BeginJob(RepositoryId repositoryId, UserId userId, string owner, string name)
    {
        _jobOwners[repositoryId] = userId;

        // Replaced outright rather than mutated: a re-analysis of a repository that previously
        // failed would otherwise start out carrying the old ErrorMessage, and the UI renders a
        // non-empty ErrorMessage as a failure regardless of IsRunning.
        var dto = new AnalysisProgressDto
        {
            RepositoryId = repositoryId,
            Owner = owner,
            Name = name,
            IsRunning = true,
            StepIndex = 0,
            StepName = "Queued",
            StepDescription = $"Queued analysis for {owner}/{name}",
            LastUpdatedUtc = DateTime.UtcNow
        };

        _progress[repositoryId] = dto;
        Publish(dto);
    }

    public void ReportStep(RepositoryId repositoryId, int stepIndex, string stepName, string stepDescription)
    {
        var dto = _progress.GetOrAdd(repositoryId, _ => new AnalysisProgressDto { RepositoryId = repositoryId });
        dto.StepIndex = stepIndex;
        dto.StepName = stepName;
        dto.StepDescription = stepDescription;
        dto.IsRunning = true;
        dto.ErrorMessage = null;
        dto.LastUpdatedUtc = DateTime.UtcNow;
        Publish(dto);
    }

    public void ReportCommitsFound(RepositoryId repositoryId, int total)
    {
        if (_progress.TryGetValue(repositoryId, out var dto))
        {
            dto.CommitsTotal = total;
            dto.CommitsProcessed = 0;
            dto.LastUpdatedUtc = DateTime.UtcNow;
            Publish(dto);
        }
    }

    public void ReportCommitProgress(RepositoryId repositoryId, int processed, int total)
    {
        if (_progress.TryGetValue(repositoryId, out var dto))
        {
            dto.CommitsProcessed = processed;
            dto.CommitsTotal = total;
            dto.LastUpdatedUtc = DateTime.UtcNow;
            Publish(dto);
        }
    }

    public void ReportComplete(RepositoryId repositoryId)
    {
        if (_progress.TryGetValue(repositoryId, out var dto))
        {
            dto.IsRunning = false;
            dto.LastUpdatedUtc = DateTime.UtcNow;
            Publish(dto);
        }

        // The snapshot stays for the polling endpoint, but the ownership entry does not: nothing
        // more will be sent for this job, and leaving it would grow the map by one per analysis
        // for the lifetime of the process.
        _jobOwners.TryRemove(repositoryId, out _);
    }

    public void ReportError(RepositoryId repositoryId, string errorMessage)
    {
        var dto = _progress.GetOrAdd(repositoryId, _ => new AnalysisProgressDto { RepositoryId = repositoryId });
        dto.IsRunning = false;
        dto.ErrorMessage = errorMessage;
        dto.LastUpdatedUtc = DateTime.UtcNow;
        Publish(dto);

        _jobOwners.TryRemove(repositoryId, out _);
    }

    public AnalysisProgressDto? GetProgress(RepositoryId repositoryId) =>
        _progress.TryGetValue(repositoryId, out var dto) ? dto : null;

    /// <summary>
    /// Sends a snapshot to the job owner's group.
    ///
    /// <para>Fire-and-forget with a swallowed failure, deliberately. These calls sit inside the
    /// analysis loop — <c>ReportCommitProgress</c> fires every five commits — and the loop's job
    /// is to finish the analysis. Awaiting would make a slow client throttle the work it is
    /// reporting on, and letting the send throw would abort an analysis because a browser tab
    /// closed. A dropped frame costs nothing: the client reconciles from the snapshot on
    /// reconnect, and the polling endpoint is still there behind it.</para>
    ///
    /// <para>A job with no recorded owner is not broadcast at all rather than broadcast to
    /// everyone — that is the case where <see cref="BeginJob"/> was skipped, and guessing would
    /// mean leaking one user's repository names to another.</para>
    /// </summary>
    private void Publish(AnalysisProgressDto dto)
    {
        if (!_jobOwners.TryGetValue(dto.RepositoryId, out var userId)) return;

        _ = SendAsync(AnalysisHub.GroupFor(userId), dto);
    }

    private async Task SendAsync(string group, AnalysisProgressDto dto)
    {
        try
        {
            await hubContext.Clients.Group(group).SendAsync(AnalysisHub.ProgressMethod, dto);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Dropped progress push for repository {RepositoryId}", dto.RepositoryId);
        }
    }
}
