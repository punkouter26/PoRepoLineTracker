using Microsoft.AspNetCore.SignalR;
using PoRepoLineTracker.API.Analysis;

namespace PoRepoLineTracker.API.Hubs;

/// <summary>
/// Drains the bounded <see cref="AnalysisHub.ProgressChannel"/> and pushes each frame to the
/// owning user's SignalR group.
///
/// <para>The split exists so the analysis loop (the writer) never awaits a slow SignalR client.
/// The channel is the backpressure boundary; this reader is the single owner of the
/// <c>IHubContext&lt;AnalysisHub&gt;</c> dependency. Failures from a wedged client are swallowed
/// at the same granularity the old fire-and-forget <c>SendAsync</c> did — one dropped frame per
/// client, never one dropped analysis.</para>
///
/// <para>Owner resolution: <see cref="IAnalysisProgressService"/> already records the
/// <see cref="UserId"/> per repository id inside <c>BeginJob</c>. Rather than expand the wire
/// shape of <see cref="Shared.Models.AnalysisProgressDto"/> to carry the owner (the DTO travels
/// to the browser), the reader asks the singleton service to resolve it. The reader does not
/// mutate the service — it only reads.</para>
/// </summary>
public sealed class AnalysisProgressReader(
    IHubContext<AnalysisHub> hubContext,
    IAnalysisProgressService progressService,
    ILogger<AnalysisProgressReader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = AnalysisHub.ProgressChannel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var frame))
                {
                    await SendAsync(frame, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown — the host is stopping, the channel reader throws on cancellation.
        }
    }

    private async Task SendAsync(Shared.Models.AnalysisProgressDto frame, CancellationToken stoppingToken)
    {
        // The producer (AnalysisProgressService.Publish) already gated on the owner map; if the
        // owner was removed between the write and the read (ReportComplete / ReportError), the
        // service has nothing left to broadcast to and the frame is dropped here.
        if (!progressService.TryGetOwner(frame.RepositoryId, out var userId)) return;

        try
        {
            await hubContext.Clients
                .Group(AnalysisHub.GroupFor(userId))
                .SendAsync(AnalysisHub.ProgressMethod, frame, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Dropped progress push for repository {RepositoryId}", frame.RepositoryId);
        }
    }
}
