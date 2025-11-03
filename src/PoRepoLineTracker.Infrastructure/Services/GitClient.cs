using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using PoRepoLineTracker.Infrastructure.Interfaces;

namespace PoRepoLineTracker.Infrastructure.Services
{
    public class GitClient : IGitClient
    {
        private readonly string? _githubPAT;

        public GitClient(IConfiguration configuration)
        {
            _githubPAT = configuration["GitHub:PAT"];
        }

        public string Clone(string repoUrl, string localPath)
        {
            var cloneOptions = new CloneOptions();

            // Configure credentials using GitHub PAT for private repositories
            if (!string.IsNullOrEmpty(_githubPAT))
            {
                cloneOptions.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
                    new UsernamePasswordCredentials
                    {
                        Username = _githubPAT, // For GitHub, the token is used as the username
                        Password = string.Empty // Password is empty when using PAT
                    };
            }

            return Repository.Clone(repoUrl, localPath, cloneOptions);
        }

        public void Pull(string localPath)
        {
            using (var repo = new Repository(localPath))
            {
                var options = new PullOptions
                {
                    FetchOptions = new FetchOptions()
                };

                // Configure credentials using GitHub PAT for private repositories
                if (!string.IsNullOrEmpty(_githubPAT))
                {
                    options.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
                        new UsernamePasswordCredentials
                        {
                            Username = _githubPAT,
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
                IEnumerable<Commit> commits = repo.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Time });

                if (sinceDate.HasValue)
                {
                    commits = commits.Where(c => c.Author.When >= sinceDate.Value);
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
