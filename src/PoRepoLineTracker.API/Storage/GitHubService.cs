using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.IO; // Added for Stream

namespace PoRepoLineTracker.API.Storage;

public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private readonly string _localReposPath;
    private readonly Dictionary<string, ILineCounter> _lineCounterMap; // New field for line counter map
    private readonly IGitClient _gitClient; // Added for DIP
    private readonly IFileIgnoreFilter _fileIgnoreFilter; // Added for file filtering

    public GitHubService(HttpClient httpClient, IConfiguration configuration, ILogger<GitHubService> logger, IEnumerable<ILineCounter> lineCounters, IGitClient gitClient, IFileIgnoreFilter fileIgnoreFilter)
    {
        _httpClient = httpClient;
        _logger = logger;
        _gitClient = gitClient; // Initialize IGitClient
        _fileIgnoreFilter = fileIgnoreFilter; // Initialize file ignore filter

        // Determine the base path for local repository clones.
        //
        // Precedence: explicit config → Azure App Service convention → local default.
        //
        // The App Service branch used to key off `HOME` being set. That is not an App Service
        // signal — it is set on virtually every developer machine (Git for Windows sets it, and
        // .NET maps it to the user profile), so a local run resolved to
        // `C:\Users\<user>\site\wwwroot\temp_repos\` — a directory that means nothing on Windows,
        // is outside the project, and burns ~30 characters of the 260-char MAX_PATH budget before
        // the repository's own paths even begin. `WEBSITE_INSTANCE_ID` is injected by App Service
        // and by nothing else, which is the check that was intended.
        var configuredPath = configuration[ConfigKeys.GitHub.LocalReposPath];
        var isAzureAppService = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            _localReposPath = configuredPath;
        }
        else if (isAzureAppService)
        {
            // App Service's writable, ephemeral per-app storage.
            var homePath = Environment.GetEnvironmentVariable("HOME") ?? "/home";
            _localReposPath = Path.Combine(homePath, "site", "wwwroot", "temp_repos");
        }
        else
        {
            _localReposPath = Path.Combine(Directory.GetCurrentDirectory(), "LocalRepos");
        }

        if (!Directory.Exists(_localReposPath))
        {
            Directory.CreateDirectory(_localReposPath);
        }

        _lineCounterMap = lineCounters.ToDictionary(lc => lc.FileExtension, lc => lc); // Initialize map
    }

    public string LocalReposBasePath => _localReposPath;

    public async Task<string> CloneRepositoryAsync(string repoUrl, string localPath, string? accessToken = null)
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

                _gitClient.Clone(repoUrl, fullLocalPath, accessToken); // Use IGitClient with access token
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

    public Task<bool> IsRepositoryValidAsync(string localPath)
    {
        var fullLocalPath = Path.Combine(_localReposPath, localPath);
        return Task.FromResult(Repository.IsValid(fullLocalPath));
    }

    public Task<bool> IsLocalRepositoryValidAsync(string fullPath)
    {
        // For locally uploaded repos, the full path is already provided (not relative to _localReposPath)
        return Task.FromResult(Repository.IsValid(fullPath));
    }

    /// <summary>
    /// Gets commit stats from a local repository at its full path, optionally since a specific date.
    /// Used for locally uploaded repositories.
    /// </summary>
    public async Task<IEnumerable<CommitStatsDto>> GetCommitStatsFromFullPathAsync(string fullPath, DateTime? sinceDate = null)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Getting commit stats for local repository at full path {FullPath} since {SinceDate}", fullPath, sinceDate);

            if (!Repository.IsValid(fullPath))
            {
                _logger.LogError("Local repository not found or invalid at {FullPath}. Cannot get commit stats.", fullPath);
                return Enumerable.Empty<CommitStatsDto>();
            }

            var commitStatsList = new List<CommitStatsDto>();

            using (var repo = _gitClient.OpenRepositoryFromPath(fullPath))
            {
                var filter = new CommitFilter
                {
                    SortBy = CommitSortStrategies.Time,
                    IncludeReachableFrom = repo.Head
                };

                IEnumerable<Commit> commits = repo.Commits.QueryBy(filter);

                // Apply date filter if provided
                if (sinceDate.HasValue)
                {
                    var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc ? sinceDate.Value : sinceDate.Value.ToUniversalTime();
                    commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
                }

                var commitsList = commits.ToList();
                _logger.LogInformation("Processing {CommitCount} commits for local repository at {FullPath}", commitsList.Count, fullPath);

                foreach (var commit in commitsList)
                {
                    int linesAdded = 0;
                    int linesRemoved = 0;

                    if (commit.Parents.Any())
                    {
                        var patch = repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree);
                        linesAdded = patch.LinesAdded;
                        linesRemoved = patch.LinesDeleted;
                    }
                    else
                    {
                        var patch = repo.Diff.Compare<Patch>(null, commit.Tree);
                        linesAdded = patch.LinesAdded;
                        linesRemoved = 0;
                    }

                    commitStatsList.Add(new CommitStatsDto
                    {
                        Sha = commit.Sha,
                        CommitDate = commit.Author.When.DateTime,
                        LinesAdded = linesAdded,
                        LinesRemoved = linesRemoved,
                        AuthorName = commit.Author.Name,
                        AuthorEmail = commit.Author.Email
                    });
                }
            }
            _logger.LogInformation("Found {CommitCount} commit stats for local repository at {FullPath}", commitStatsList.Count, fullPath);
            return commitStatsList.AsEnumerable();
        });
    }

    /// <summary>
    /// Counts lines in a commit for a local repository at its full path.
    /// Used for locally uploaded repositories.
    /// </summary>
    public async Task<Dictionary<string, int>> CountLinesInCommitFromFullPathAsync(string fullPath, string commitSha, IEnumerable<string> fileExtensionsToCount)
    {
        _logger.LogInformation("Counting lines for commit {CommitSha} in local repository at {FullPath}. File extensions to count: {FileExtensions}", commitSha, fullPath, string.Join(", ", fileExtensionsToCount));

        if (!Repository.IsValid(fullPath))
        {
            _logger.LogError("Local repository not found or invalid at {FullPath}. Cannot count lines.", fullPath);
            return new Dictionary<string, int>();
        }

        var lineCounts = new Dictionary<string, int>();

        using (var repo = _gitClient.OpenRepositoryFromPath(fullPath))
        {
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                _logger.LogWarning("Commit {CommitSha} not found in local repository at {FullPath}", commitSha, fullPath);
                return lineCounts;
            }

            if (commit.Tree != null)
            {
                await ProcessTreeEntry(commit.Tree, fileExtensionsToCount, lineCounts, "");
            }
            else
            {
                _logger.LogWarning("Commit {CommitSha} has a null tree. Skipping line counting.", commitSha);
            }

            _logger.LogInformation("Finished counting lines for commit {CommitSha}. Total lines by type: {LineCounts}", commitSha, lineCounts);
        }
        return lineCounts;
    }

    public Task DeleteLocalRepositoryAsync(string localPath)
    {
        var fullLocalPath = Path.Combine(_localReposPath, localPath);
        if (Directory.Exists(fullLocalPath))
        {
            _logger.LogInformation("Deleting local repository directory {LocalPath} for re-clone", fullLocalPath);
            Directory.Delete(fullLocalPath, recursive: true);
        }
        return Task.CompletedTask;
    }

    public async Task<string> PullRepositoryAsync(string localPath, string? accessToken = null)
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

            _gitClient.Pull(fullLocalPath, accessToken); // Use IGitClient with access token

            _logger.LogInformation("Successfully pulled repository at {LocalPath}", fullLocalPath);
            return fullLocalPath;
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

        using (var repo = _gitClient.OpenRepository(fullLocalPath)) // Use IGitClient
        {
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                _logger.LogWarning("Commit {CommitSha} not found in repository at {LocalPath}", commitSha, fullLocalPath);
                return lineCounts;
            }

            // Read directly from the git object store (no working-tree checkout needed)
            if (commit.Tree != null)
            {
                // Use a recursive function to traverse the tree
                await ProcessTreeEntry(commit.Tree, fileExtensionsToCount, lineCounts, "");
            }
            else
            {
                _logger.LogWarning("Commit {CommitSha} has a null tree. Skipping line counting.", commitSha);
            }

            _logger.LogInformation("Finished counting lines for commit {CommitSha}. Total lines by type: {LineCounts}", commitSha, lineCounts);
        }
        return lineCounts;
    }

    private async Task ProcessTreeEntry(Tree tree, IEnumerable<string> fileExtensionsToCount, Dictionary<string, int> lineCounts, string currentPath = "")
    {
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(currentPath) ? entry.Name : $"{currentPath}/{entry.Name}";

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // The subtree is resolved BEFORE the ignore check so the filter can see what the
                // directory contains, not just what it is called. That is what identifies a
                // third-party repository copied in under a name the author chose — see
                // FileIgnoreFilter.IsEmbeddedRepositoryRoot.
                var subTree = entry.Target as Tree;

                if (subTree is null
                        ? _fileIgnoreFilter.ShouldIgnoreDirectory(entryPath)
                        : _fileIgnoreFilter.ShouldIgnoreDirectory(entryPath, subTree.Select(e => e.Name)))
                {
                    continue;
                }

                // Recursively process subdirectories
                _logger.LogDebug("Traversing directory: {DirectoryName}", entryPath);
                if (subTree is not null)
                {
                    await ProcessTreeEntry(subTree, fileExtensionsToCount, lineCounts, entryPath);
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                // Check if this file should be ignored using the new filter
                if (_fileIgnoreFilter.ShouldIgnoreFile(entry.Name, entryPath))
                {
                    continue;
                }

                var fileName = entry.Name;
                var fileExtension = Path.GetExtension(fileName.ToLowerInvariant());

                // Now check if it's in our allowed extensions
                if (fileExtensionsToCount.Contains(fileExtension))
                {
                    var blob = entry.Target as Blob;
                    if (blob != null)
                    {
                        _logger.LogDebug("Processing file: {FileName}, Size: {FileSize} bytes", entry.Name, blob.Size);

                        int lines = await CountBlobLinesAsync(blob, fileExtension);
                        _logger.LogDebug("Counted {Lines} lines for file {FileName}.", lines, entry.Name);

                        if (lineCounts.ContainsKey(fileExtension))
                        {
                            lineCounts[fileExtension] += lines;
                        }
                        else
                        {
                            lineCounts[fileExtension] = lines;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Tree entry {EntryName} is not a blob, or blob is null.", entry.Name);
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping file {FileName} with extension {FileExtension} as it's not in the list of extensions to count.", entry.Name, fileExtension);
                }
            }
            else
            {
                _logger.LogDebug("Skipping tree entry {EntryName} as it is not a blob or tree (Type: {TargetType}).", entry.Name, entry.TargetType);
            }
        }
    }

    /// <summary>
    /// Counts a single blob's lines with the strategy registered for its extension, falling back
    /// to the "*" strategy for anything without a dedicated one.
    /// </summary>
    private async Task<int> CountBlobLinesAsync(Blob blob, string fileExtension)
    {
        if (!_lineCounterMap.TryGetValue(fileExtension, out var lineCounter))
        {
            _lineCounterMap.TryGetValue("*", out lineCounter);
        }

        if (lineCounter == null)
        {
            _logger.LogWarning("No line counter found for file extension {FileExtension}.", fileExtension);
            return 0;
        }

        using var contentStream = blob.GetContentStream();
        return await lineCounter.CountLinesAsync(contentStream);
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

            // #2 fix: use _gitClient abstraction instead of direct Repository instantiation (DIP)
            using (var repo = _gitClient.OpenRepository(fullLocalPath))
            {
                var filter = new CommitFilter
                {
                    SortBy = CommitSortStrategies.Time,
                    IncludeReachableFrom = repo.Head
                };

                IEnumerable<Commit> commits = repo.Commits.QueryBy(filter);

                // Apply date filter if provided
                if (sinceDate.HasValue)
                {
                    _logger.LogInformation("Filtering commits since {SinceDate} (UTC)", sinceDate.Value);
                    // Convert sinceDate to UTC for comparison with commit dates
                    var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc ? sinceDate.Value : sinceDate.Value.ToUniversalTime();
                    commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
                }

                var commitsList = commits.ToList();
                _logger.LogInformation("Processing {CommitCount} commits after date filtering", commitsList.Count);

                foreach (var commit in commitsList)
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
                        LinesRemoved = linesRemoved,
                        AuthorName = commit.Author.Name,
                        AuthorEmail = commit.Author.Email
                    });
                }
            }
            _logger.LogInformation("Found {CommitCount} commit stats for repository at {LocalPath}", commitStatsList.Count, fullLocalPath);
            return commitStatsList.AsEnumerable();
        });
    }

    public async Task CheckConnectionAsync()
    {
        _logger.LogInformation("Checking GitHub API connection.");
        // Make a simple unauthenticated request to the GitHub API root to check connectivity
        var response = await _httpClient.GetAsync("/");
        response.EnsureSuccessStatusCode(); // Throws an exception if the HTTP response status code is not 2xx
        _logger.LogInformation("GitHub API connection successful.");
    }

    public async Task<IEnumerable<GitHubUserRepositoryDto>> GetUserRepositoriesAsync(string accessToken)
    {
        _logger.LogInformation("Fetching user repositories from GitHub API.");

        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogError("Access token is required to fetch user repositories.");
            throw new InvalidOperationException("Access token is required to fetch user repositories.");
        }

        try
        {
            // Create a new request with the user's access token
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos?type=owner&sort=name&direction=asc&per_page=100");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd("PoRepoLineTracker");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            var repoData = System.Text.Json.JsonSerializer.Deserialize<List<GitHubApiRepository>>(jsonContent);

            var userRepositories = repoData?.Select(repo =>
            {
                var fullName = repo.full_name ?? string.Empty;
                var slashIndex = fullName.IndexOf('/');
                var owner = slashIndex > 0 ? fullName[..slashIndex] : string.Empty;
                return new GitHubUserRepositoryDto
                {
                    Name = repo.name ?? string.Empty,
                    Owner = owner,
                    FullName = fullName,
                    CloneUrl = repo.clone_url ?? string.Empty,
                    Description = repo.description ?? string.Empty,
                    IsPrivate = repo.@private,
                    Language = repo.language ?? string.Empty
                };
            }) ?? Enumerable.Empty<GitHubUserRepositoryDto>();

            _logger.LogInformation("Successfully fetched {RepositoryCount} user repositories from GitHub API.", userRepositories.Count());
            return userRepositories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user repositories from GitHub API: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    // Private class for deserializing GitHub API response
    private class GitHubApiRepository
    {
        public string? name { get; set; }
        public string? full_name { get; set; }
        public string? clone_url { get; set; }
        public string? description { get; set; }
        public bool @private { get; set; }
        public string? language { get; set; }
    }
}
