using System.Diagnostics;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Infrastructure.Interfaces;

namespace PoRepoLineTracker.Infrastructure.Services
{
    /// <summary>
    /// Git client that uses the git CLI for network operations (clone/pull) to avoid
    /// LibGit2Sharp native SIGABRT crashes in containerized Linux environments, while
    /// retaining LibGit2Sharp for local read-only operations (traversing commits/blobs).
    /// </summary>
    public class GitClient : IGitClient
    {
        private readonly ILogger<GitClient> _logger;

        public GitClient(IConfiguration configuration, ILogger<GitClient> logger)
        {
            _logger = logger;
        }

        public string Clone(string repoUrl, string localPath, string? accessToken = null)
        {
            _logger.LogInformation("Cloning repository {RepoUrl} to {LocalPath} via git CLI", repoUrl, localPath);

            // Embed token in URL for HTTPS authentication (x-access-token is the standard GitHub approach)
            string cloneUrl = BuildAuthUrl(repoUrl, accessToken);

            RunGitProcess("clone", ["clone", "--quiet", "--", cloneUrl, localPath], workingDirectory: null);

            _logger.LogInformation("Successfully cloned {RepoUrl} to {LocalPath}", repoUrl, localPath);
            return localPath;
        }

        public void Pull(string localPath, string? accessToken = null)
        {
            _logger.LogInformation("Pulling repository at {LocalPath} via git CLI", localPath);

            // For pull, configure token via remote URL if needed
            if (!string.IsNullOrEmpty(accessToken))
            {
                // Get the current remote URL and embed the token, then restore after pull
                RunGitProcess("remote get-url", ["remote", "get-url", "origin"], workingDirectory: localPath,
                    captureOutput: true, out string remoteUrl);

                string authUrl = BuildAuthUrl(remoteUrl.Trim(), accessToken);
                RunGitProcess("remote set-url (auth)", ["remote", "set-url", "origin", authUrl], workingDirectory: localPath);
                try
                {
                    RunGitProcess("pull", ["pull", "--quiet"], workingDirectory: localPath);
                }
                finally
                {
                    // Restore original (token-free) remote URL
                    RunGitProcess("remote set-url (restore)", ["remote", "set-url", "origin", remoteUrl.Trim()], workingDirectory: localPath);
                }
            }
            else
            {
                RunGitProcess("pull", ["pull", "--quiet"], workingDirectory: localPath);
            }

            _logger.LogInformation("Successfully pulled repository at {LocalPath}", localPath);
        }

        public IEnumerable<(string Sha, DateTimeOffset CommitDate)> GetCommits(string localPath, DateTime? sinceDate = null)
        {
            using var repo = new Repository(localPath);

            IEnumerable<Commit> commits = repo.Commits.QueryBy(new CommitFilter
            {
                SortBy = CommitSortStrategies.Time,
                IncludeReachableFrom = repo.Head
            });

            if (sinceDate.HasValue)
            {
                var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc
                    ? sinceDate.Value
                    : sinceDate.Value.ToUniversalTime();
                commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
            }

            return commits.Select(c => (c.Sha, c.Author.When)).ToList();
        }

        public Repository OpenRepository(string localPath)
        {
            return new Repository(localPath);
        }

        public void Checkout(Repository repo, Commit commit)
        {
            Commands.Checkout(repo, commit);
        }

        // ── Private helpers ─────────────────────────────────────────────────────────

        private static string BuildAuthUrl(string repoUrl, string? accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
                return repoUrl;

            var uri = new Uri(repoUrl);
            return $"{uri.Scheme}://x-access-token:{accessToken}@{uri.Host}{uri.PathAndQuery}";
        }

        private void RunGitProcess(string operationLabel, string[] arguments, string? workingDirectory)
            => RunGitProcess(operationLabel, arguments, workingDirectory, captureOutput: false, out _);

        private void RunGitProcess(string operationLabel, string[] arguments, string? workingDirectory,
            bool captureOutput, out string output)
        {
            _logger.LogDebug("Running git {Operation} (args: {ArgCount})", operationLabel, arguments.Length);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            // Prevent git from hanging waiting for a terminal credential prompt
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

            Process process;
            try
            {
                process = Process.Start(psi)
                    ?? throw new InvalidOperationException($"Process.Start returned null for git {operationLabel}");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                _logger.LogError(ex, "git executable not found in PATH. Cannot perform git {Operation}. " +
                    "Ensure git is installed in the container.", operationLabel);
                throw new InvalidOperationException(
                    "git is not installed or not in PATH. " +
                    $"Cannot perform git {operationLabel}. Install git in the deployment environment.", ex);
            }

            using (process)
            {
                // Read both streams concurrently to avoid deadlock on full buffers
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                output = stdoutTask.Result;
                var stderr = stderrTask.Result;

                if (process.ExitCode != 0)
                {
                    // Sanitize any embedded credentials before logging
                    var safeStderr = RedactCredentials(stderr);
                    _logger.LogError("git {Operation} failed. ExitCode={ExitCode}. Stderr: {Stderr}",
                        operationLabel, process.ExitCode, safeStderr);
                    throw new InvalidOperationException(
                        $"git {operationLabel} failed (exit {process.ExitCode}): {safeStderr}");
                }

                _logger.LogDebug("git {Operation} succeeded. ExitCode=0", operationLabel);
            }
        }

        /// <summary>Redacts any https://user:password@ patterns to prevent leaking access tokens in logs.</summary>
        private static string RedactCredentials(string text)
            => Regex.Replace(text, @"https?://[^:@\s]+:[^@\s]+@", "https://[REDACTED]@",
                RegexOptions.IgnoreCase);
    }
}
