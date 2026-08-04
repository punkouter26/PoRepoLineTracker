using System.Net.Http.Json;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Client for reading and persisting user preferences via the settings API.
/// Caches the result of the first successful GET so that multiple components
/// on the same page (e.g. ChartDisplayModeCard + RepositoryDetail) share a
/// single HTTP round-trip instead of issuing redundant parallel requests.
/// The cache is invalidated on every successful SaveAsync call.
/// </summary>
public sealed class UserPreferencesClient(HttpClient httpClient)
{
    // Guard against concurrent initial fetches from multiple components.
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private UserPreferences? _cached;

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

            _cached = await httpClient.GetAppJsonAsync<UserPreferences>("/api/settings/user-preferences", cancellationToken)
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
        var response = await httpClient.PutAppJsonAsync("/api/settings/user-preferences", preferences, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Server returned {response.StatusCode}: {error}");
        }

        var saved = await response.Content.ReadAppJsonAsync<UserPreferences>(cancellationToken)
            ?? preferences;

        // Invalidate cache so the next GetAsync reflects the persisted value.
        _cached = saved;
        return saved;
    }
}