using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// Service for managing user accounts and authentication state.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Gets a user by their internal ID.
    /// </summary>
    Task<User?> GetUserByIdAsync(Guid userId);

    /// <summary>
    /// Gets a user by their GitHub ID.
    /// </summary>
    Task<User?> GetUserByGitHubIdAsync(string gitHubId);

    /// <summary>
    /// Creates or updates a user based on GitHub OAuth data.
    /// If user exists, updates their token and last login time.
    /// </summary>
    Task<User> UpsertUserAsync(User user);

    /// <summary>
    /// Updates a user's access token.
    /// </summary>
    Task UpdateAccessTokenAsync(Guid userId, string accessToken, DateTime? expiresAt = null);

    /// <summary>
    /// Gets the access token for a user.
    /// </summary>
    Task<string?> GetAccessTokenAsync(Guid userId);

    /// <summary>
    /// Deletes a user and optionally their associated data.
    /// </summary>
    Task DeleteUserAsync(Guid userId, bool deleteAssociatedData = false);
}
