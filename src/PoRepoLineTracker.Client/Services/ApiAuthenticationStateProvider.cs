using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using PoRepoLineTracker.Shared.Models;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Custom AuthenticationStateProvider that checks user authentication status via API.
/// Persists GUEST sessions in LocalStorage so they survive browser refreshes and E2E tests.
/// </summary>
public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private AuthResponse? _cachedUser;
    private const string GuestStorageKey = "PoRepoLineTracker.GuestSession";

    public ApiAuthenticationStateProvider(HttpClient httpClient, IJSRuntime jsRuntime)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Gets the current user information if authenticated.
    /// </summary>
    public AuthResponse? CurrentUser => _cachedUser;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/auth/me");

            if (response.IsSuccessStatusCode)
            {
                _cachedUser = await response.Content.ReadFromJsonAsync<AuthResponse>();

                if (_cachedUser?.IsAuthenticated == true && !string.IsNullOrEmpty(_cachedUser.UserId))
                {
                    // Persist GUEST sessions in LocalStorage for refresh/test resilience
                    if (_cachedUser.IsAnon)
                    {
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", GuestStorageKey,
                            System.Text.Json.JsonSerializer.Serialize(_cachedUser));
                    }

                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, _cachedUser.UserId),
                        new(ClaimTypes.Name, _cachedUser.Username ?? "User"),
                    };

                    if (!string.IsNullOrEmpty(_cachedUser.DisplayName))
                        claims.Add(new Claim("DisplayName", _cachedUser.DisplayName));

                    if (!string.IsNullOrEmpty(_cachedUser.Email))
                        claims.Add(new Claim(ClaimTypes.Email, _cachedUser.Email));

                    if (!string.IsNullOrEmpty(_cachedUser.AvatarUrl))
                        claims.Add(new Claim("AvatarUrl", _cachedUser.AvatarUrl));

                    if (_cachedUser.IsAnon)
                        claims.Add(new Claim("IsAnon", "true"));

                    var identity = new ClaimsIdentity(claims, "GitHub");
                    var user = new ClaimsPrincipal(identity);

                    return new AuthenticationState(user);
                }
            }
        }
        catch (HttpRequestException)
        {
            // API not available — try LocalStorage GUEST fallback for dev/E2E
            try
            {
                var guestJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", GuestStorageKey);
                if (!string.IsNullOrEmpty(guestJson))
                {
                    _cachedUser = System.Text.Json.JsonSerializer.Deserialize<AuthResponse>(guestJson);
                    if (_cachedUser != null)
                    {
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, _cachedUser.UserId ?? "guest"),
                            new(ClaimTypes.Name, _cachedUser.Username ?? "GUEST"),
                            new("IsAnon", "true")
                        };
                        var identity = new ClaimsIdentity(claims, "Guest");
                        return new AuthenticationState(new ClaimsPrincipal(identity));
                    }
                }
            }
            catch { /* LocalStorage not available */ }
        }

        _cachedUser = null;
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    /// <summary>
    /// Notifies the application that the authentication state has changed.
    /// Call this after login/logout to refresh the UI.
    /// </summary>
    public void NotifyAuthenticationStateChanged()
    {
        _cachedUser = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
