using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Infrastructure.Interfaces;
using System.Diagnostics;

namespace PoRepoLineTracker.Infrastructure.Services
{
    public class GitClient : IGitClient
    {
        private readonly ILogger<GitClient> _logger;

        public GitClient(IConfiguration configuration, ILogger<GitClient> logger)
        {
            _logger = logger;
        }

        public string Clone(string repoUrl, string localPath, string? accessToken = null)
        {
            // Build authenticated URL without logging the token
            string cloneUrl = repoUrl;
            if (!string.IsNullOrEmpty(accessToken))
            {
                var uri = new Uri(repoUrl);
                cloneUrl = $"https://{accessToken}@{uri.Host}{uri.AbsolutePath}";
            }

            _logger.LogInformation("Cloning repository {RepoUrl} to {LocalPath} via git CLI", repoUrl, localPath);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone \"{cloneUrl}\" \"{localPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var safeError = stderr.Replace(accessToken ?? string.Empty, "***");
                _logger.LogError("git clone failed (exit {Code}): {Error}", process.ExitCode, safeError);
                throw new InvalidOperationException($"git clone failed (exit {process.ExitCode}): {safeError}");
            }

            _logger.LogInformation("Successfully cloned to {LocalPath}", localPath);
            return localPath;
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
