namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// Represents an authenticated user in the system.
/// Users are identified by their GitHub account.
/// </summary>
public class User
{
    /// <summary>
    /// Unique identifier for the user in our system.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// GitHub user ID (numeric, from GitHub API).
    /// </summary>
    public string GitHubId { get; set; } = string.Empty;

    /// <summary>
    /// GitHub username (login name).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// User's display name from GitHub.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// User's email from GitHub (may be null if private).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// URL to the user's GitHub avatar.
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// GitHub OAuth access token (encrypted at rest).
    /// Used to access GitHub API on behalf of the user.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// When the user first signed up.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the user last logged in.
    /// </summary>
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Token expiration time (if applicable).
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }
}
