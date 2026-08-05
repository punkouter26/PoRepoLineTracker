using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Blazor-side facade over <c>wwwroot/js/gfx.js</c> — the WebGL mesh backdrop, the canvas
/// activity ribbon, and the View Transitions route animation.
///
/// <para><b>Lifetime is the caller's problem, and this class makes that explicit.</b> Every
/// <c>Start*</c> here begins a <c>requestAnimationFrame</c> loop that holds a GPU context and will
/// run until stopped. A component that starts one MUST stop it from <c>IDisposable.Dispose</c> /
/// <c>DisposeAsync</c>. The JS side keys its handles off the canvas element and stops any previous
/// loop for that element on re-start, which makes a missed stop survivable — but only for the same
/// element. Navigate away and the element is discarded while the loop keeps drawing into a
/// detached canvas, and Chrome evicts the oldest WebGL context once ~16 are live.</para>
///
/// <para>Registered as Scoped, which in a WASM host is the lifetime of the app. It holds only the
/// module reference; all per-canvas state lives in JS.</para>
/// </summary>
public sealed class GfxService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly ILogger<GfxService>? _logger;

    private IJSObjectReference? _module;
    private Task<IJSObjectReference>? _moduleTask;
    private bool _unavailable;

    public GfxService(IJSRuntime js, ILogger<GfxService>? logger = null)
    {
        _js = js;
        _logger = logger;
    }

    private async ValueTask<IJSObjectReference?> TryGetModuleAsync()
    {
        if (_unavailable) return null;
        if (_module is not null) return _module;

        try
        {
            _moduleTask ??= _js.InvokeAsync<IJSObjectReference>("import", "./js/gfx.js").AsTask();
            _module = await _moduleTask;
            return _module;
        }
        catch (JSException ex)
        {
            _unavailable = true;
            _logger?.LogDebug(ex, "Graphics module unavailable; falling back to CSS decoration.");
            return null;
        }
        catch (InvalidOperationException)
        {
            // Interop before the JS runtime is attached. Not latched — retried next render.
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the animated mesh backdrop on <paramref name="canvas"/>.
    /// </summary>
    /// <param name="theme">"dark" or "light" — selects the shader palette.</param>
    /// <returns>
    /// True when a canvas renderer (WebGL or 2D) actually took over. False means the caller should
    /// leave the CSS gradient fallback visible: either the module is missing, or
    /// <c>prefers-reduced-motion</c> is set, in which case NOT animating is the correct outcome.
    /// </returns>
    public async Task<bool> StartBackdropAsync(ElementReference canvas, string theme)
    {
        var module = await TryGetModuleAsync();
        if (module is null) return false;

        try
        {
            return await module.InvokeAsync<bool>("startBackdrop", canvas, theme);
        }
        catch (JSException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    /// <summary>Starts the rolling throughput ribbon. <paramref name="accent"/> must be a 6-digit
    /// hex colour with a leading '#' — the JS appends two alpha digits to build the fill gradient,
    /// so a 3-digit or named colour produces an invalid colour string and a blank strip.</summary>
    public async Task<bool> StartRibbonAsync(ElementReference canvas, string accent)
    {
        var module = await TryGetModuleAsync();
        if (module is null) return false;

        try
        {
            return await module.InvokeAsync<bool>("startRibbon", canvas, accent);
        }
        catch (JSException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    /// <summary>Feeds the ribbon one 0..1 sample. Safe to call before the ribbon starts.</summary>
    public async Task PushRibbonAsync(ElementReference canvas, double value)
    {
        if (_module is null) return;   // deliberately not importing on this path — it is per-frame

        try
        {
            await _module.InvokeVoidAsync("pushRibbon", canvas, value);
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    /// <summary>Stops whatever loop is running on this canvas and releases its GL context.</summary>
    public async Task StopAsync(ElementReference canvas)
    {
        if (_module is null) return;

        try
        {
            await _module.InvokeVoidAsync("stop", canvas);
        }
        catch (JSDisconnectedException) { /* page already gone; the context went with it */ }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// Wraps the next render in <c>document.startViewTransition</c>. No-ops where the API is
    /// missing or reduced motion is requested, in which case the navigation still happens — only
    /// the animation is skipped.
    /// </summary>
    public async Task RouteTransitionAsync()
    {
        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeAsync<bool>("routeTransition");
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }

        _module = null;
        _moduleTask = null;
    }
}
