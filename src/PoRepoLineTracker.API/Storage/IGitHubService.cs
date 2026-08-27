
namespace PoRepoLineTracker.API.Storage;

public interface IGitHubService
{
    /// <summary>
    /// Base directory local repository clones live under — Azure App Service ephemeral storage
    /// when running there, otherwise the configured or default local path. The single source for
    /// resolving a repo's on-disk location; do not re-derive this from the HOME environment
    /// variable elsewhere.
    /// </summary>
    string LocalReposBasePath { get; }

    /// <summary>
    /// Turns a stored <c>LocalPath</c> (relative to <see cref="LocalReposBasePath"/>) into the
    /// absolute path the read operations below take.
    ///
    /// <para>This exists so there is ONE place a repository's location is decided. Every read
    /// method used to come in two versions — one taking a relative path and one taking an
    /// absolute one, for uploaded repositories whose <c>LocalPath</c> is already absolute — which
    /// pushed a three-way branch on "which kind of repository is this" into every caller and
    /// doubled the interface. Callers now resolve once and pass an absolute path.</para>
    /// </summary>
    string ResolveRepositoryPath(string localPath);

    Task<string> CloneRepositoryAsync(string repoUrl, string localPath, string? accessToken = null);
    Task<string> PullRepositoryAsync(string localPath, string? accessToken = null);

    /// <summary>
    /// Deletes the local repository directory so it can be re-cloned from scratch.
    /// </summary>
    Task DeleteLocalRepositoryAsync(string localPath);

    // ── Reads. All take an ABSOLUTE repository path — see ResolveRepositoryPath. ──────────────

    /// <summary>Whether an absolute path holds a valid git repository.</summary>
    Task<bool> IsRepositoryValidAsync(string repositoryPath);

    Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string repositoryPath, DateTime? sinceDate = null);
    Task<Dictionary<string, int>> CountLinesInCommitAsync(string repositoryPath, string commitSha, IEnumerable<string> fileExtensionsToCount);

    /// <summary>
    /// The text of every counted source file at one commit, subject to the same ignore rules the
    /// line counter applies.
    ///
    /// <para>Lazy and synchronous, deliberately. Lazy because a caller that materialised every
    /// file of a large repository would hold the whole checkout in memory at once, where measuring
    /// them one at a time holds one; synchronous because LibGit2Sharp is, and wrapping each blob
    /// read in a Task would add machinery around work that never yields. Callers doing this off a
    /// request thread should wrap the whole enumeration in one <c>Task.Run</c>.</para>
    ///
    /// <para>The git repository stays open for as long as the enumeration does, so it must be
    /// enumerated to completion or disposed.</para>
    /// </summary>
    IEnumerable<SourceFile> EnumerateSourceFiles(string repositoryPath, string commitSha, IEnumerable<string> fileExtensionsToCount);

    Task CheckConnectionAsync();
    Task<IEnumerable<GitHubUserRepositoryDto>> GetUserRepositoriesAsync(string accessToken);
}
