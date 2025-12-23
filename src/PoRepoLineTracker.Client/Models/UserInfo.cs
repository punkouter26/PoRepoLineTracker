namespace PoRepoLineTracker.Client.Models;

/// <summary>
/// Represents the authentication status response from the API.
/// </summary>
public sealed record AuthResponse(
    bool IsAuthenticated,
    string? UserId = null,
    string? Username = null,
    string? DisplayName = null,
    string? Email = null,
    string? AvatarUrl = null);
