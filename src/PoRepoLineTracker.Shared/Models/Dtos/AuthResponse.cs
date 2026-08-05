namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Represents the authentication status response returned by /auth/me.
/// Shared between the API (return type) and the Blazor WASM client (deserialization target).
/// </summary>
public sealed record AuthResponse(
    bool IsAuthenticated,
    string? UserId = null,
    string? Username = null,
    string? DisplayName = null,
    string? Email = null,
    string? AvatarUrl = null);
