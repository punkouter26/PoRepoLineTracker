using LibGit2Sharp;
using PoRepoLineTracker.Infrastructure.Interfaces;

namespace PoRepoLineTracker.Infrastructure.Services
{
    public class GitClient : IGitClient
    {
        public string Clone(string repoUrl, string localPath)
        {
            return Repository.Clone(repoUrl, localPath);
        }

        public void Pull(string localPath)
        {
            using (var repo = new Repository(localPath))
            {
                // For simplicity, this pull assumes a single remote named "origin" and a single branch.
                // In a real-world scenario, you might need more sophisticated logic for credentials,
                // branch tracking, and merge strategies.
                var remote = repo.Network.Remotes["origin"];
                var options = new PullOptions();
                // You might need to configure credentials here if the repo is private
                // options.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
                //     new UsernamePasswordCredentials { Username = "your_username", Password = "your_pat" };

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
