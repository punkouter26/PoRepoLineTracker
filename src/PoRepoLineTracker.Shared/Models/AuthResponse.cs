namespace PoRepoLineTracker.Shared.Models;

/// <summary>
/// Represents the authentication status response returned by /api/auth/me.
/// Shared between the API (return type) and the Blazor WASM client (deserialization target).
/// </summary>
public sealed record AuthResponse(
    bool IsAuthenticated,
    string? UserId = null,
    string? Username = null,
    string? DisplayName = null,
    string? Email = null,
    string? AvatarUrl = null);
