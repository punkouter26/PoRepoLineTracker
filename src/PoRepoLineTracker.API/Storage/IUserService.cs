
namespace PoRepoLineTracker.API.Storage;

/// <summary>
/// Service for managing user accounts and authentication state.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Gets a user by their internal ID.
    /// </summary>
    Task<User?> GetUserByIdAsync(UserId userId);

    /// <summary>
    /// Creates or updates a user based on GitHub OAuth data.
    /// If user exists, updates their token and last login time.
    /// </summary>
    Task<User> UpsertUserAsync(User user);
}
