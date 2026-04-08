using System.Net.Http.Json;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Client.Services;

public sealed class UserPreferencesClient(HttpClient httpClient)
{
    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<UserPreferences>("/api/settings/user-preferences", cancellationToken)
            ?? new UserPreferences();
    }

    public async Task<UserPreferences> SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync("/api/settings/user-preferences", preferences, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Server returned {response.StatusCode}: {error}");
        }

        return await response.Content.ReadFromJsonAsync<UserPreferences>(cancellationToken)
            ?? preferences;
    }
}