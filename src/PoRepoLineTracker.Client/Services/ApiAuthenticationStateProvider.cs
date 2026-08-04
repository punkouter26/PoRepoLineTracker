using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using PoRepoLineTracker.Shared.Models;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// Custom AuthenticationStateProvider that checks user authentication status via API.
/// </summary>
public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private AuthResponse? _cachedUser;

    public ApiAuthenticationStateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gets the current user information if authenticated.
    /// </summary>
    public AuthResponse? CurrentUser => _cachedUser;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("auth/me");

            if (response.IsSuccessStatusCode)
            {
                _cachedUser = await response.Content.ReadAppJsonAsync<AuthResponse>();

                if (_cachedUser?.IsAuthenticated == true && !string.IsNullOrEmpty(_cachedUser.UserId))
                {
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

                    var identity = new ClaimsIdentity(claims, "GitHub");
                    return new AuthenticationState(new ClaimsPrincipal(identity));
                }
            }
        }
        catch (HttpRequestException)
        {
            // API not available — fall through to the unauthenticated state below.
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
