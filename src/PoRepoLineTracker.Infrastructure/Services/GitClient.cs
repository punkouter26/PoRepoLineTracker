using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using PoRepoLineTracker.Infrastructure.Interfaces;

namespace PoRepoLineTracker.Infrastructure.Services
{
    public class GitClient : IGitClient
    {
        public GitClient(IConfiguration configuration)
        {
            // No longer store PAT - tokens are passed per-request
        }

        public string Clone(string repoUrl, string localPath, string? accessToken = null)
        {
            var cloneOptions = new CloneOptions();

            // Configure credentials using access token for private repositories
            if (!string.IsNullOrEmpty(accessToken))
            {
                cloneOptions.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
                    new UsernamePasswordCredentials
                    {
                        Username = accessToken, // For GitHub, the token is used as the username
                        Password = string.Empty // Password is empty when using token
                    };
            }

            return Repository.Clone(repoUrl, localPath, cloneOptions);
        }

        public void Pull(string localPath, string? accessToken = null)
        {
            using (var repo = new Repository(localPath))
            {
                var options = new PullOptions
                {
                    FetchOptions = new FetchOptions()
                };

                // Configure credentials using access token for private repositories
                if (!string.IsNullOrEmpty(accessToken))
                {
                    options.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
                        new UsernamePasswordCredentials
                        {
                            Username = accessToken,
                            Password = string.Empty
                        };
                }

                Commands.Pull(repo, new Signature("PoRepoLineTracker", "noreply@example.com", DateTimeOffset.Now), options);
            }
        }

        public IEnumerable<(string Sha, DateTimeOffset CommitDate)> GetCommits(string localPath, DateTime? sinceDate = null)
        {
            using (var repo = new Repository(localPath))
            {
                IEnumerable<Commit> commits = repo.Commits.QueryBy(new CommitFilter
                {
                    SortBy = CommitSortStrategies.Time,
                    IncludeReachableFrom = repo.Head
                });

                if (sinceDate.HasValue)
                {
                    // Convert sinceDate to UTC for consistent comparison
                    var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc ? sinceDate.Value : sinceDate.Value.ToUniversalTime();
                    commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
                }

                return commits.Select(c => (c.Sha, c.Author.When)).ToList();
            }
        }

        public Repository OpenRepository(string localPath)
        {
            return new Repository(localPath);
        }

        public void Checkout(Repository repo, Commit commit)
        {
            Commands.Checkout(repo, commit);
        }
    }
}
