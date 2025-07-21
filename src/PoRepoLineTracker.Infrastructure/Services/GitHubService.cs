using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Infrastructure.Services;

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private readonly string _localReposPath;
    private readonly string? _gitHubPat;

    public GitHubService(HttpClient httpClient, IConfiguration configuration, ILogger<GitHubService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _localReposPath = configuration["GitHub:LocalReposPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "LocalRepos");
        _gitHubPat = configuration["GitHub:PAT"];

        if (!Directory.Exists(_localReposPath))
        {
            Directory.CreateDirectory(_localReposPath);
        }

        if (!string.IsNullOrEmpty(_gitHubPat))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_gitHubPat}");
        }
    }

    public async Task<string> CloneRepositoryAsync(string repoUrl, string localPath)
    {
        return await Task.Run(() =>
        {
            var fullLocalPath = Path.Combine(_localReposPath, localPath);
            _logger.LogInformation("Attempting to clone repository {RepoUrl} to {LocalPath}", repoUrl, fullLocalPath);

            if (Repository.IsValid(fullLocalPath))
            {
                _logger.LogInformation("Repository already exists at {LocalPath}. Skipping clone.", fullLocalPath);
                return fullLocalPath;
            }

            try
            {
                // Ensure the parent directory exists
                var parentDir = Path.GetDirectoryName(fullLocalPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                var cloneOptions = new CloneOptions();
                if (!string.IsNullOrEmpty(_gitHubPat))
                {
                    cloneOptions.FetchOptions.CredentialsProvider = (url, user, cred) =>
                        new UsernamePasswordCredentials { Username = _gitHubPat, Password = "" };
                }

                Repository.Clone(repoUrl, fullLocalPath, cloneOptions);
                _logger.LogInformation("Successfully cloned repository {RepoUrl} to {LocalPath}", repoUrl, fullLocalPath);
                return fullLocalPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clone repository {RepoUrl} to {LocalPath}. Error: {ErrorMessage}", repoUrl, fullLocalPath, ex.Message);
                throw;
            }
        });
    }

    public async Task<string> PullRepositoryAsync(string localPath)
    {
        return await Task.Run(() =>
        {
            var fullLocalPath = Path.Combine(_localReposPath, localPath);
            _logger.LogInformation("Attempting to pull repository at {LocalPath}", fullLocalPath);

            if (!Repository.IsValid(fullLocalPath))
            {
                _logger.LogError("Local repository not found or invalid at {LocalPath}. Cannot pull.", fullLocalPath);
                throw new DirectoryNotFoundException($"Local repository not found or invalid at {fullLocalPath}");
            }

            using (var repo = new Repository(fullLocalPath))
            {
                // Fetch all remote branches
                Commands.Fetch(repo, repo.Network.Remotes["origin"].Name, new string[0], new FetchOptions
                {
                    CredentialsProvider = (url, user, cred) =>
                        new UsernamePasswordCredentials { Username = _gitHubPat, Password = "" }
                }, null);

                // Determine the default branch (main or master)
                var remoteHead = repo.Branches.FirstOrDefault(b => b.IsRemote && (b.FriendlyName == "origin/main" || b.FriendlyName == "origin/master"));
                if (remoteHead == null)
                {
                    _logger.LogError("Could not find remote 'main' or 'master' branch for repository at {LocalPath}", fullLocalPath);
                    throw new InvalidOperationException("Could not find remote 'main' or 'master' branch.");
                }

                // Checkout the local tracking branch or create it if it doesn't exist
                var localBranch = repo.Branches[remoteHead.FriendlyName.Replace("origin/", "")];
                if (localBranch == null)
                {
                    localBranch = repo.CreateBranch(remoteHead.FriendlyName.Replace("origin/", ""), remoteHead.Tip);
                    repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteHead.CanonicalName);
                }

                Commands.Checkout(repo, localBranch);

                // Reset the local branch to the fetched remote branch's tip
                repo.Reset(ResetMode.Hard, remoteHead.Tip);

                _logger.LogInformation("Successfully fetched and reset repository at {LocalPath} to {Branch}", fullLocalPath, remoteHead.FriendlyName);
                return fullLocalPath;
            }
        });
    }

    public async Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsAsync(string localPath, DateTime? sinceDate = null)
    {
        return await Task.Run(() =>
        {
            var fullLocalPath = Path.Combine(_localReposPath, localPath);
            _logger.LogInformation("Getting commits for repository at {LocalPath} since {SinceDate}", fullLocalPath, sinceDate);

            if (!Repository.IsValid(fullLocalPath))
            {
                _logger.LogError("Local repository not found or invalid at {LocalPath}. Cannot get commits.", fullLocalPath);
                return Enumerable.Empty<(string Sha, DateTimeOffset CommitDate)>();
            }

            using (var repo = new Repository(fullLocalPath))
            {
                var commits = repo.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Time })
                                          .Select(c => (c.Sha, c.Author.When))
                                          .ToList();
                _logger.LogInformation("Found {CommitCount} new commits for repository at {LocalPath}", commits.Count, fullLocalPath);
                return commits.AsEnumerable();
            }
        });
    }

    public async Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount)
    {
        var fullLocalPath = Path.Combine(_localReposPath, localPath);
        _logger.LogInformation("Counting lines for commit {CommitSha} in repository at {LocalPath}. File extensions to count: {FileExtensions}", commitSha, fullLocalPath, string.Join(", ", fileExtensionsToCount));

        if (!Repository.IsValid(fullLocalPath))
        {
            _logger.LogError("Local repository not found or invalid at {LocalPath}. Cannot count lines.", fullLocalPath);
            return new Dictionary<string, int>();
        }

        var lineCounts = new Dictionary<string, int>();

        using (var repo = new Repository(fullLocalPath))
        {
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                _logger.LogWarning("Commit {CommitSha} not found in repository at {LocalPath}", commitSha, fullLocalPath);
                return lineCounts;
            }

            // Checkout the specific commit to count lines
            Commands.Checkout(repo, commit);

            if (commit.Tree != null)
            {
                // Use a recursive function to traverse the tree
                await ProcessTreeEntry(commit.Tree, fileExtensionsToCount, lineCounts);
            }
            else
            {
                _logger.LogWarning("Commit {CommitSha} has a null tree. Skipping line counting.", commitSha);
            }

            _logger.LogInformation("Finished counting lines for commit {CommitSha}. Total lines by type: {LineCounts}", commitSha, lineCounts);
        }
        return lineCounts;
    }

    private async Task ProcessTreeEntry(Tree tree, IEnumerable<string> fileExtensionsToCount, Dictionary<string, int> lineCounts)
    {
        foreach (var entry in tree)
        {
            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var fileExtension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (fileExtensionsToCount.Contains(fileExtension))
                {
                    var blob = entry.Target as Blob;
                    if (blob != null)
                    {
                        _logger.LogInformation("Processing file: {FileName}, Size: {FileSize} bytes", entry.Name, blob.Size);
                        using (var contentStream = blob.GetContentStream())
                        using (var reader = new StreamReader(contentStream))
                        {
                            int lines = 0;
                            string? line;
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                lines++;
                            }
                            _logger.LogInformation("Counted {Lines} lines for file {FileName}.", lines, entry.Name);

                            if (lineCounts.ContainsKey(fileExtension))
                            {
                                lineCounts[fileExtension] += lines;
                            }
                            else
                            {
                                lineCounts[fileExtension] = lines;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Tree entry {EntryName} is not a blob, or blob is null.", entry.Name);
                    }
                }
                else
                {
                    _logger.LogInformation("Skipping file {FileName} with extension {FileExtension} as it's not in the list of extensions to count.", entry.Name, fileExtension);
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // Recursively process subdirectories
                _logger.LogInformation("Traversing directory: {DirectoryName}", entry.Name);
                await ProcessTreeEntry(entry.Target as Tree, fileExtensionsToCount, lineCounts);
            }
            else
            {
                _logger.LogInformation("Skipping tree entry {EntryName} as it is not a blob or tree (Type: {TargetType}).", entry.Name, entry.TargetType);
            }
        }
    }

    public async Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string localPath, DateTime? sinceDate = null)
    {
        return await Task.Run(() =>
        {
            var fullLocalPath = Path.Combine(_localReposPath, localPath);
            _logger.LogInformation("Getting commit stats for repository at {LocalPath} since {SinceDate}", fullLocalPath, sinceDate);

            if (!Repository.IsValid(fullLocalPath))
            {
                _logger.LogError("Local repository not found or invalid at {LocalPath}. Cannot get commit stats.", fullLocalPath);
                return Enumerable.Empty<CommitStatsDto>();
            }

            var commitStatsList = new List<CommitStatsDto>();

            using (var repo = new Repository(fullLocalPath))
            {
                var filter = new CommitFilter
                {
                    SortBy = CommitSortStrategies.Time
                };

                var commits = repo.Commits.QueryBy(filter)
                                          .ToList();

                foreach (var commit in commits)
                {
                    int linesAdded = 0;
                    int linesRemoved = 0;

                    if (commit.Parents.Any())
                    {
                        var patch = repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree);
                        linesAdded = patch.LinesAdded;
                        linesRemoved = patch.LinesDeleted;
                        _logger.LogDebug("Commit {CommitSha}: LinesAdded={LinesAdded}, LinesRemoved={LinesRemoved}", commit.Sha, linesAdded, linesRemoved);
                    }
                    else
                    {
                        // Initial commit, count all lines as added
                        var patch = repo.Diff.Compare<Patch>(null, commit.Tree);
                        linesAdded = patch.LinesAdded;
                        linesRemoved = 0; // No lines removed in initial commit
                        _logger.LogDebug("Initial Commit {CommitSha}: LinesAdded={LinesAdded}, LinesRemoved={LinesRemoved}", commit.Sha, linesAdded, linesRemoved);
                    }

                    commitStatsList.Add(new CommitStatsDto
                    {
                        Sha = commit.Sha,
                        CommitDate = commit.Author.When.DateTime,
                        LinesAdded = linesAdded,
                        LinesRemoved = linesRemoved
                    });
                }
            }
            _logger.LogInformation("Found {CommitCount} commit stats for repository at {LocalPath}", commitStatsList.Count, fullLocalPath);
            return commitStatsList.AsEnumerable();
        });
    }

    public async Task<long> GetTotalLinesOfCodeAsync(string localPath, IEnumerable<string> fileExtensionsToCount)
    {
        var fullLocalPath = Path.Combine(_localReposPath, localPath);
        _logger.LogInformation("Counting total lines of code for repository at {LocalPath}. File extensions to count: {FileExtensions}", fullLocalPath, string.Join(", ", fileExtensionsToCount));

        if (!Repository.IsValid(fullLocalPath))
        {
            _logger.LogError("Local repository not found or invalid at {LocalPath}. Cannot count total lines.", fullLocalPath);
            return 0;
        }

        long totalLines = 0;

        try
        {
            using (var repo = new Repository(fullLocalPath))
            {
                // Get the current HEAD commit's tree
                var headCommit = repo.Head.Tip;
                if (headCommit == null || headCommit.Tree == null)
                {
                    _logger.LogWarning("Repository at {LocalPath} has no HEAD commit or tree. Returning 0 lines.", fullLocalPath);
                    return 0;
                }

                // Recursively count lines in the current tree
                totalLines = await CountLinesInTreeAsync(headCommit.Tree, fileExtensionsToCount);
            }
            _logger.LogInformation("Total lines of code for {LocalPath}: {TotalLines}", fullLocalPath, totalLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting total lines of code for {LocalPath}: {ErrorMessage}", fullLocalPath, ex.Message);
        }

        return totalLines;
    }

    private async Task<long> CountLinesInTreeAsync(Tree tree, IEnumerable<string> fileExtensionsToCount)
    {
        long lines = 0;
        foreach (var entry in tree)
        {
            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var fileExtension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (fileExtensionsToCount.Contains(fileExtension))
                {
                    var blob = entry.Target as Blob;
                    if (blob != null)
                    {
                        using (var contentStream = blob.GetContentStream())
                        using (var reader = new StreamReader(contentStream))
                        {
                            while (await reader.ReadLineAsync() != null)
                            {
                                lines++;
                            }
                        }
                    }
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                lines += await CountLinesInTreeAsync(entry.Target as Tree, fileExtensionsToCount);
            }
        }
        return lines;
    }

    public async Task CheckConnectionAsync()
    {
        _logger.LogInformation("Checking GitHub API connection.");
        // Make a simple unauthenticated request to the GitHub API root to check connectivity
        var response = await _httpClient.GetAsync("/");
        response.EnsureSuccessStatusCode(); // Throws an exception if the HTTP response status code is not 2xx
        _logger.LogInformation("GitHub API connection successful.");
    }
}
