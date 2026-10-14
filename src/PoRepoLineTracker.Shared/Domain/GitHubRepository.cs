namespace PoRepoLineTracker.Shared.Domain;

public class GitHubRepository
{
    public RepositoryId Id { get; set; }

    /// <summary>
    /// The user who owns this repository tracking entry.
    /// Used to partition repositories by authenticated user.
    /// </summary>
    public UserId UserId { get; set; }

    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public DateTime? LastAnalyzedCommitDate { get; set; } // Nullable to avoid Azure Table Storage DateTime.MinValue issue
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// When the last analysis run finished, whether or not it succeeded. Distinct from
    /// <see cref="LastAnalyzedCommitDate"/>, which is the newest COMMIT's date and stays null for
    /// an empty repository or one that cannot be cloned — rows the startup resume sweep would
    /// otherwise retry on every restart.
    /// </summary>
    public DateTime? LastAnalysisAttemptUtc { get; set; }

    /// <summary>Why the last run failed; null when it succeeded or has not run.</summary>
    public string? LastAnalysisError { get; set; }

    /// <summary>
    /// The only clone URL the server uses: built from owner/name, never accepted from a caller.
    /// The user's token is embedded in whatever URL git is handed, so a caller-supplied URL sent
    /// that token to a host of the caller's choosing. Validate owner/name
    /// (<c>RepositoryValidators.IsGitHubName</c>) before handing the result to git.
    /// </summary>
    public static string CloneUrlFor(string owner, string name) => $"https://github.com/{owner}/{name}.git";

    /// <summary>
    /// The clone's directory under the clone root, derived from the id alone so analysis can
    /// re-clone after an ephemeral-filesystem restart and delete can find it without the row.
    /// </summary>
    public static string CloneDirectoryName(RepositoryId id) => $"repo_{id}";
}
