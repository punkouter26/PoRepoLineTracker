using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// Service for managing user preferences.
/// </summary>
public interface IUserPreferencesService
{
    /// <summary>
    /// Gets preferences for a user. Returns default preferences if none exist.
    /// </summary>
    Task<UserPreferences> GetPreferencesAsync(Guid userId);

    /// <summary>
    /// Saves user preferences.
    /// </summary>
    Task SavePreferencesAsync(UserPreferences preferences);

    /// <summary>
    /// Gets file extensions for a specific user.
    /// </summary>
    Task<List<string>> GetFileExtensionsAsync(Guid userId);
}
