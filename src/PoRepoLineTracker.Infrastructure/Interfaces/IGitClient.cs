using LibGit2Sharp;

namespace PoRepoLineTracker.Infrastructure.Interfaces
{
    public interface IGitClient
    {
        /// <summary>
        /// Clones a remote repository to a local path.
        /// </summary>
        /// <param name="repoUrl">The URL of the remote repository.</param>
        /// <param name="localPath">The local path where the repository should be cloned.</param>
        /// <param name="accessToken">Optional: OAuth access token for authentication.</param>
        /// <returns>The path to the cloned repository.</returns>
        string Clone(string repoUrl, string localPath, string? accessToken = null);

        /// <summary>
        /// Pulls changes from the remote repository for a given local repository.
        /// </summary>
        /// <param name="localPath">The local path of the repository.</param>
        /// <param name="accessToken">Optional: OAuth access token for authentication.</param>
        void Pull(string localPath, string? accessToken = null);

        /// <summary>
        /// Gets all commits from a local repository, optionally since a specific date.
        /// </summary>
        /// <param name="localPath">The local path of the repository.</param>
        /// <param name="sinceDate">Optional: Only return commits after this date.</param>
        /// <returns>An enumerable of commit SHAs and their commit dates.</returns>
        IEnumerable<(string Sha, DateTimeOffset CommitDate)> GetCommits(string localPath, DateTime? sinceDate = null);

        /// <summary>
        /// Opens an existing local repository.
        /// </summary>
        /// <param name="localPath">The local path of the repository.</param>
        /// <returns>A LibGit2Sharp Repository object.</returns>
        Repository OpenRepository(string localPath);

        /// <summary>
        /// Checks out a specific commit in the given repository.
        /// </summary>
        /// <param name="repo">The repository instance.</param>
        /// <param name="commit">The commit to checkout.</param>
        void Checkout(Repository repo, Commit commit);
    }
}
