using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace PoRepoLineTracker.Infrastructure.Services;

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private readonly string _localReposPath;
    private readonly string? _gitHubPat;

    public GitHubService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<GitHubService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GitHubClient");
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
    }

    public async Task<string> PullRepositoryAsync(string localPath)
    {
        var fullLocalPath = Path.Combine(_localReposPath, localPath);
        _logger.LogInformation("Attempting to pull repository at {LocalPath}", fullLocalPath);

        if (!Repository.IsValid(fullLocalPath))
        {
            _logger.LogError("Local repository not found or invalid at {LocalPath}. Cannot pull.", fullLocalPath);
            throw new DirectoryNotFoundException($"Local repository not found or invalid at {fullLocalPath}");
        }

        try
        {
            using (var repo = new Repository(fullLocalPath))
            {
                var options = new PullOptions
                {
                    FetchOptions = new FetchOptions
                    {
                        CredentialsProvider = (url, user, cred) =>
                            new UsernamePasswordCredentials { Username = _gitHubPat, Password = "" }
                    }
                };

                var signature = new Signature("PoRepoLineTracker", "porepolinetracker@example.com", DateTimeOffset.Now);
                Commands.Pull(repo, signature, options);
            }
            _logger.LogInformation("Successfully pulled repository at {LocalPath}", fullLocalPath);
            return fullLocalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull repository at {LocalPath}. Error: {ErrorMessage}", fullLocalPath, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<(string Sha, DateTimeOffset CommitDate)>> GetCommitsAsync(string localPath, DateTime? sinceDate = null)
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
                                      .Where(c => !sinceDate.HasValue || c.Author.When > sinceDate.Value)
                                      .Select(c => (c.Sha, c.Author.When))
                                      .ToList();
            _logger.LogInformation("Found {CommitCount} new commits for repository at {LocalPath}", commits.Count, fullLocalPath);
            return commits;
        }
    }

    public async Task<Dictionary<string, int>> CountLinesInCommitAsync(string localPath, string commitSha, IEnumerable<string> fileExtensionsToCount)
    {
        var fullLocalPath = Path.Combine(_localReposPath, localPath);
        _logger.LogInformation("Counting lines for commit {CommitSha} in repository at {LocalPath}", commitSha, fullLocalPath);

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

            foreach (var entry in commit.Tree)
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
                                int lines = 0;
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line)) // Exclude blank lines
                                    {
                                        lines++;
                                    }
                                }

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
                    }
                }
            }
            _logger.LogInformation("Finished counting lines for commit {CommitSha}. Total lines by type: {LineCounts}", commitSha, lineCounts);
        }
        return lineCounts;
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
