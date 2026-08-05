using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// The cue vocabulary. A closed enum rather than free strings so a typo is a
/// compile error instead of a sound that silently never plays — the failure mode
/// is invisible at runtime, since a missing cue is indistinguishable from muted.
/// </summary>
public enum Cue
{
    /// <summary>Generic button / row activation.</summary>
    Click,
    /// <summary>A switch moving to its on state.</summary>
    ToggleOn,
    /// <summary>A switch moving to its off state.</summary>
    ToggleOff,
    /// <summary>Route change.</summary>
    Nav,
    /// <summary>A long-running job finished cleanly.</summary>
    Success,
    /// <summary>A job failed, or a destructive action was rejected.</summary>
    Error,
    /// <summary>Validation rejected the input; nothing was lost.</summary>
    Warn,
    /// <summary>One unit of progress on a running job. Rate-limited in JS.</summary>
    Tick,
    /// <summary>A long-running job was accepted and has begun.</summary>
    Start
}

/// <summary>
/// Blazor-side facade over <c>wwwroot/js/audio.js</c>.
///
/// <para><b>Why a service rather than direct IJSRuntime calls.</b> Three reasons that all bite in
/// practice: the module must be imported exactly once (each <c>import()</c> of the same specifier
/// returns the same module instance, but each round trip still costs an interop call on a hot
/// path); every call site would otherwise have to repeat the null/pre-render guard; and the
/// AudioContext must be resumed from a real user gesture, which is a single concern this class
/// owns via <see cref="UnlockAsync"/>.</para>
///
/// <para><b>Every method swallows JS exceptions.</b> Audio is decoration. A browser with Web Audio
/// disabled, a context the autoplay policy refuses to resume, or a module that failed to fetch
/// must all degrade to silence — never to an error boundary over the user's dashboard. The
/// specific catches are documented at each site.</para>
///
/// <para><b>Interop stays primitive-only.</b> No parameter or return value here is a complex type.
/// The client is published trimmed with the reflection-based JSON resolver removed
/// (<c>AppJsonSerializerContext</c> is the only resolver), but Blazor's JS interop layer uses its
/// own reflection-based options — passing an object across the boundary would reintroduce exactly
/// the reflection the trimming configuration exists to eliminate.</para>
/// </summary>
public sealed class AudioService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly ILogger<AudioService>? _logger;

    private IJSObjectReference? _module;
    private Task<IJSObjectReference>? _moduleTask;
    private bool _unlocked;
    private bool _unavailable;

    public AudioService(IJSRuntime js, ILogger<AudioService>? logger = null)
    {
        _js = js;
        _logger = logger;
    }

    /// <summary>True once a gesture has successfully resumed the AudioContext.</summary>
    public bool IsUnlocked => _unlocked;

    /// <summary>
    /// Mirrors the JS-side enabled flag so components can render a toggle without an interop
    /// round trip per frame. Seeded by <see cref="InitializeAsync"/>.
    /// </summary>
    public bool Enabled { get; private set; }

    public double Volume { get; private set; } = 0.5;

    public bool Ambient { get; private set; }

    public double AmbientVolume { get; private set; } = 0.18;

    /// <summary>False when the browser has no AudioContext at all; the settings UI disables itself.</summary>
    public bool Supported { get; private set; } = true;

    /// <summary>
    /// Lazily imports the module. Cached as the <see cref="Task{TResult}"/> rather than the
    /// resolved reference so concurrent first-callers await one import instead of racing to start
    /// several — the activity feed and the layout both touch this within the same render pass.
    /// </summary>
    private Task<IJSObjectReference>? ModuleAsync()
    {
        if (_unavailable) return null;
        return _moduleTask ??= _js.InvokeAsync<IJSObjectReference>(
            "import", "./js/audio.js").AsTask();
    }

    private async ValueTask<IJSObjectReference?> TryGetModuleAsync()
    {
        if (_unavailable) return null;
        if (_module is not null) return _module;

        try
        {
            var task = ModuleAsync();
            if (task is null) return null;
            _module = await task;
            return _module;
        }
        catch (JSException ex)
        {
            // The module itself failed to parse or fetch. Nothing about the app depends on it, so
            // latch it off rather than retrying on every subsequent cue.
            _unavailable = true;
            _logger?.LogDebug(ex, "Audio module unavailable; UI sound disabled for this session.");
            return null;
        }
        catch (InvalidOperationException)
        {
            // Thrown when interop is attempted during prerender, before the JS runtime is attached.
            // Not latched: the next call after first render will succeed.
            return null;
        }
        catch (TaskCanceledException)
        {
            // Circuit/host torn down mid-import.
            return null;
        }
    }

    /// <summary>
    /// Reads the persisted preferences out of JS so the C# mirror starts in step with them.
    /// Call once from <c>OnAfterRenderAsync(firstRender: true)</c>.
    /// </summary>
    public async Task InitializeAsync()
    {
        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            Supported      = await module.InvokeAsync<bool>("isSupported");
            Enabled        = await module.InvokeAsync<bool>("getEnabled");
            Volume         = await module.InvokeAsync<double>("getVolume");
            Ambient        = await module.InvokeAsync<bool>("getAmbient");
            AmbientVolume  = await module.InvokeAsync<double>("getAmbientVolume");
        }
        catch (JSException ex)
        {
            _logger?.LogDebug(ex, "Could not read audio preferences; using defaults.");
        }
        catch (TaskCanceledException) { /* teardown */ }
    }

    /// <summary>
    /// Installs the document-level delegated click/keydown cues. Idempotent on the JS side, so a
    /// layout that re-renders cannot stack listeners. Call once from the layout's first render.
    /// </summary>
    public async Task InstallGlobalCuesAsync()
    {
        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("installGlobalCues");
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// Resumes the AudioContext. MUST be reached from a user gesture — a call from a timer or a
    /// SignalR callback will be refused by the browser's autoplay policy, and the refusal is
    /// silent. Cheap to call repeatedly; it no-ops once unlocked.
    /// </summary>
    public async Task UnlockAsync()
    {
        if (_unlocked || !Enabled) return;

        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            _unlocked = await module.InvokeAsync<bool>("unlock");
        }
        catch (JSException) { /* policy refused; stays silent */ }
        catch (TaskCanceledException) { /* teardown */ }
    }

    /// <summary>
    /// Plays a cue. Fire-and-forget by design: awaiting a decorative sound would put an interop
    /// round trip on the click path ahead of the actual handler.
    /// </summary>
    /// <param name="gain">Per-call attenuation, 0..1, on top of the master volume.</param>
    public async Task PlayAsync(Cue cue, double gain = 1.0)
    {
        if (!Enabled) return;

        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("play", CueName(cue), gain);
        }
        catch (JSException) { /* decoration; never surfaces */ }
        catch (TaskCanceledException) { /* teardown */ }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        Enabled = enabled;

        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("setEnabled", enabled);

            // Enabling IS the gesture — this is only ever reached from a click on the toggle, so
            // it is the one legitimate opportunity to resume the context without a second
            // interaction. Without this the first cue after opting in is swallowed.
            if (enabled)
            {
                _unlocked = await module.InvokeAsync<bool>("unlock");
                await module.InvokeVoidAsync("play", CueName(Cue.ToggleOn), 1.0);
            }
            else
            {
                _unlocked = false;
            }
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async Task SetVolumeAsync(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);

        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("setVolume", Volume);
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async Task SetAmbientAsync(bool on)
    {
        Ambient = on;

        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("setAmbient", on);
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async Task SetAmbientVolumeAsync(double volume)
    {
        AmbientVolume = Math.Clamp(volume, 0, 1);

        var module = await TryGetModuleAsync();
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("setAmbientVolume", AmbientVolume);
        }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// Explicit mapping rather than <c>cue.ToString()</c>: <c>ToString()</c> on an enum goes
    /// through reflection over the type's metadata, which is precisely what the trimmer is
    /// configured to strip. A switch expression compiles to a jump table over constants.
    /// </summary>
    private static string CueName(Cue cue) => cue switch
    {
        Cue.Click     => "click",
        Cue.ToggleOn  => "toggleOn",
        Cue.ToggleOff => "toggleOff",
        Cue.Nav       => "nav",
        Cue.Success   => "success",
        Cue.Error     => "error",
        Cue.Warn      => "warn",
        Cue.Tick      => "tick",
        Cue.Start     => "start",
        _             => "click"
    };

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;

        try
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { /* page is already gone */ }
        catch (JSException) { }
        catch (TaskCanceledException) { }

        _module = null;
        _moduleTask = null;
    }
}
