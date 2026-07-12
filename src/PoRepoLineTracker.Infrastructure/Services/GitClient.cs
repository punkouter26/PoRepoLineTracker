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

            // Embed token in URL for HTTPS authentication (x-access-token is the standard GitHub approach).
            // Sanity-check the token shape: only GitHub PATs (ghp_, gho_, ghu_, ghs_, ghr_) belong in
            // a GitHub clone URL. Anything else (notably a Microsoft Graph JWT, which is ~1.5KB and
            // starts with 'EwBY' or 'eyJ') would be silently rejected by libcurl with a confusing
            // "Port number" error, so we surface a clear diagnostic before invoking git.
            if (!string.IsNullOrEmpty(accessToken) && !LooksLikeGitHubToken(accessToken))
            {
                _logger.LogWarning(
                    "Access token for clone of {RepoUrl} does not look like a GitHub PAT (length={Length}, prefix='{Prefix}'). Git will likely reject it. " +
                    "Microsoft Graph JWTs (saved by Microsoft OAuth) must NOT be used as a GitHub credential — configure GitHub:PAT in Key Vault instead.",
                    repoUrl, accessToken.Length, SafePrefix(accessToken));
            }

            string cloneUrl = BuildAuthUrl(repoUrl, accessToken);

            // Analysis reads exclusively from the git object store (commit/tree/blob traversal
            // via LibGit2Sharp — see GitHubService.ProcessTreeEntry), so the working tree is never
            // read. Skip the checkout: it avoids materializing deeply-nested files that blow past
            // Windows' 260-char MAX_PATH ("path too long"), and it clones faster with less disk.
            RunGitProcess("clone", ["clone", "--quiet", "--no-checkout", "--", cloneUrl, localPath], workingDirectory: null);

            _logger.LogInformation("Successfully cloned {RepoUrl} to {LocalPath}", repoUrl, localPath);
            return localPath;
        }

        private static bool LooksLikeGitHubToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            // Classic PATs: ghp_/gho_/ghu_/ghs_/ghr_  (length 36-40+ in practice)
            // Fine-grained PATs: github_pat_  (length 82+)
            if (token.StartsWith("ghp_", StringComparison.Ordinal) ||
                token.StartsWith("gho_", StringComparison.Ordinal) ||
                token.StartsWith("ghu_", StringComparison.Ordinal) ||
                token.StartsWith("ghs_", StringComparison.Ordinal) ||
                token.StartsWith("ghr_", StringComparison.Ordinal) ||
                token.StartsWith("github_pat_", StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }

        private static string SafePrefix(string token)
            => token.Length <= 8 ? token : token.Substring(0, 4) + "…" + token.Substring(token.Length - 4);

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

        public IEnumerable<(string Sha, DateTimeOffset CommitDate)> GetCommitsFromPath(string fullPath, DateTime? sinceDate = null)
        {
            // Just use the full path directly
            return GetCommits(fullPath, sinceDate);
        }

        public Repository OpenRepositoryFromPath(string fullPath)
        {
            return new Repository(fullPath);
        }

        // ── Private helpers ─────────────────────────────────────────────────────────

        private static string BuildAuthUrl(string repoUrl, string? accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
                return repoUrl;

            var uri = new Uri(repoUrl);

            // URL-encode the token so any reserved characters (':', '/', '+', '=', etc.)
            // cannot confuse libcurl's URL parser. Without this, a Microsoft Graph JWT
            // embedded as a GitHub "token" produces "Port number was not a decimal
            // number" because libcurl misreads part of the JWT as `host:port`.
            var encodedToken = Uri.EscapeDataString(accessToken);
            return $"{uri.Scheme}://x-access-token:{encodedToken}@{uri.Host}{uri.PathAndQuery}";
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
