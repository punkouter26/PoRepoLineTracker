using System.Net.Http.Json;
using PoRepoLineTracker.Shared.Domain;
using PoRepoLineTracker.Shared.Serialization;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Client for reading and persisting user preferences via the settings API.
/// Caches the result of the first successful GET so that multiple components
/// on the same page share a single HTTP round-trip instead of issuing redundant
/// parallel requests. The cache is refreshed on every successful SaveAsync call.
///
/// <para>This is the single source of truth for a saved preference. Pages read it
/// here and subscribe to <see cref="Changed"/>; none of them render their own copy
/// of a settings control. That is not a style point — the chart display mode used
/// to be rendered as a card on the repositories page AND inside Settings, each with
/// its own local <c>_chartDisplayMode</c> field, so changing it in one place left
/// the other showing the old mode until a reload.</para>
/// </summary>
public sealed class UserPreferencesClient(HttpClient httpClient)
{
    // Guard against concurrent initial fetches from multiple components.
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private UserPreferences? _cached;

    /// <summary>
    /// Raised after preferences are successfully persisted, so every page holding a
    /// derived value updates without a reload. Async-returning rather than a plain
    /// EventHandler because subscribers need to marshal onto the renderer's context
    /// (<c>InvokeAsync(StateHasChanged)</c>) and often refetch as well.
    /// </summary>
    public event Func<UserPreferences, Task>? Changed;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            // Double-checked locking: another caller may have fetched while we waited.
            if (_cached is not null)
                return _cached;

            _cached = await httpClient.GetAppJsonAsync("/api/settings/user-preferences", AppJsonSerializerContext.Default.UserPreferences, cancellationToken)
                ?? new UserPreferences();

            return _cached;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public async Task<UserPreferences> SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAppJsonAsync(
            "/api/settings/user-preferences", preferences, AppJsonSerializerContext.Default.UserPreferences, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Server returned {response.StatusCode}: {error}");
        }

        var saved = await response.Content.ReadAppJsonAsync(AppJsonSerializerContext.Default.UserPreferences, cancellationToken)
            ?? preferences;

        // Refresh the cache so the next GetAsync reflects the persisted value.
        _cached = saved;

        if (Changed is not null)
        {
            // Sequentially, and each subscriber isolated: one page that throws while
            // re-rendering must not stop the others from seeing the new value, and must
            // not surface as a failed save to the caller — the write already succeeded.
            foreach (var handler in Changed.GetInvocationList().Cast<Func<UserPreferences, Task>>())
            {
                try
                {
                    await handler(saved);
                }
                catch
                {
                    // Notifying a subscriber is best-effort; the persisted value is authoritative.
                }
            }
        }

        return saved;
    }
}