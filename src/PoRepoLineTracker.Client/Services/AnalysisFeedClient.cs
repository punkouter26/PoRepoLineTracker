using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Shared.Models;
using PoRepoLineTracker.Shared.Serialization;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// One shared connection to <c>/hubs/analysis</c>, fanned out to every component that wants live
/// analysis progress.
///
/// <para><b>Why a service and not a HubConnection per component.</b> Both the repository grid and
/// the activity feed want the same stream, and they mount and unmount independently. A connection
/// per consumer would mean two WebSockets, two handshakes and — because the page can render the
/// feed and the grid at once — two copies of every frame. Registered scoped, which in a WASM host
/// is the lifetime of the app.</para>
///
/// <para><b>Trimming.</b> The hub protocol is given the app's source-generated resolver rather
/// than the reflection default. Without this the payload would be (de)serialized through the
/// reflection resolver, which is exactly what <c>PublishTrimmed</c> removes from this assembly —
/// producing a client that works in development and fails only once published.</para>
/// </summary>
public sealed class AnalysisFeedClient(NavigationManager navigation, ILogger<AnalysisFeedClient> logger)
    : IAsyncDisposable
{
    private HubConnection? _connection;
    private Task<bool>? _starting;

    /// <summary>Raised for every progress frame. Handlers run on the hub's callback context.</summary>
    public event Func<AnalysisProgressDto, Task>? ProgressReceived;

    /// <summary>
    /// Raised when the hub starts reconnecting, comes back, or closes for good.
    /// <see cref="IsConnected"/> is a property nobody is told about: without this the feed's
    /// "Streaming / Reconnecting…" label only caught up when the next frame happened to arrive,
    /// and the fallback poll never learned the hub had gone. Runs on the hub's callback context.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>True once the hub is connected — the UI uses this to decide whether to poll.</summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Connects if not already connected, and returns whether the connection is usable.
    ///
    /// <para>Concurrent callers share one attempt via <see cref="_starting"/>: the grid and the
    /// feed both call this on first render, and two simultaneous <c>StartAsync</c> calls on one
    /// connection throw.</para>
    ///
    /// <para>Returns false rather than throwing when the hub is unreachable. The caller's fallback
    /// is to poll, which is a normal degraded mode — not an error worth surfacing to the user.</para>
    ///
    /// <para>A failed attempt is forgotten once its callers have their answer. <c>_starting</c>
    /// used to keep the completed <c>false</c> task for the lifetime of the app, so one failed
    /// handshake (server restarting, a blip at sign-in) meant polling until a full reload.</para>
    /// </summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        var attempt = _starting ??= ConnectAsync();
        var connected = await attempt;

        // Only clear the attempt that was awaited — a later caller may already have started the retry.
        if (!connected && _starting == attempt) _starting = null;
        return connected;
    }

    private async Task<bool> ConnectAsync()
    {
        try
        {
            // A retry replaces the connection; the dead one still holds its handlers and timers.
            // Detached first so its Closed handler below no longer counts as "the" connection.
            if (_connection is { } dead)
            {
                _connection = null;
                await dead.DisposeAsync();
            }

            var connection = _connection = new HubConnectionBuilder()
                .WithUrl(navigation.ToAbsoluteUri("/hubs/analysis"))
                // Analysis runs for minutes; a dropped connection has to come back on its own or
                // the feed silently stops mid-job. The default policy retries at 0s/2s/10s/30s and
                // then gives up, which is the right shape for a page left open.
                .WithAutomaticReconnect()
                .AddJsonProtocol(options =>
                {
                    // Inserted at the front of the chain rather than assigned over it: the
                    // protocol seeds the chain with its own converters for the envelope types,
                    // and replacing it wholesale drops those.
                    options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(
                        0, AppJsonSerializerContext.Default);
                })
                .Build();

            connection.On<AnalysisProgressDto>(ProgressMethod, async progress =>
            {
                if (ProgressReceived is not null)
                    await ProgressReceived.Invoke(progress);
            });

            connection.Reconnecting += _ => RaiseStateChanged();
            connection.Reconnected += _ => RaiseStateChanged();
            // Closed means automatic reconnect gave up. Forget the attempt so the next
            // EnsureConnectedAsync builds a fresh connection instead of answering "connected"
            // from a task that completed before the drop.
            connection.Closed += _ =>
            {
                if (_connection == connection) _starting = null;
                return RaiseStateChanged();
            };

            await connection.StartAsync();
            logger.LogInformation("Analysis feed connected");
            return true;
        }
        catch (Exception ex)
        {
            // Anonymous sessions, a proxy that blocks WebSockets, or the hub simply not being
            // there yet all land here. The pages that use this fall back to polling.
            logger.LogWarning(ex, "Analysis feed unavailable — falling back to polling");
            return false;
        }
    }

    private Task RaiseStateChanged()
    {
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Must match <c>AnalysisHub.ProgressMethod</c>. Not shared: the hub lives in the API assembly.</summary>
    private const string ProgressMethod = "AnalysisProgress";

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
