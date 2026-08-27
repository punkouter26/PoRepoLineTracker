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
    private readonly Dictionary<string, ILineCounter> _lineCounterMap;
    private readonly GitClient _gitClient;
    private readonly FileIgnoreFilter _fileIgnoreFilter;

    /// <summary>
    /// Per-analysis memos, keyed by git object id. Safe to hold on the instance because this
    /// service is scoped and an analysis run owns its scope — see CountTreeAsync for what they buy
    /// and InvalidateCachesIfExtensionsChanged for when they are dropped.
    ///
    /// <para>Plain dictionaries, not concurrent ones: the analysis loop processes commits one at a
    /// time on a single background task, and a per-repository semaphore in the analyze handler
    /// keeps a second run off the same repository. Nothing here is touched from two threads.</para>
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, int>> _treeLineCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _blobLineCounts = new(StringComparer.Ordinal);
    private string _memoExtensionSignature = string.Empty;

    /// <summary>
    /// Cap on either memo before it is cleared. Sized so an ordinary repository's whole history
    /// fits comfortably, while a bulk import of very large repositories still cannot grow the
    /// process without bound.
    /// </summary>
    private const int MaxMemoEntries = 200_000;

    public GitHubService(HttpClient httpClient, IConfiguration configuration, ILogger<GitHubService> logger, IEnumerable<ILineCounter> lineCounters, GitClient gitClient, FileIgnoreFilter fileIgnoreFilter)
    {
        _httpClient = httpClient;
        _logger = logger;
        _gitClient = gitClient;
        _fileIgnoreFilter = fileIgnoreFilter;

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

    /// <summary>
    /// An absolute path is returned unchanged; a relative one is resolved against the base
    /// directory. Uploaded repositories store an absolute <c>LocalPath</c> and cloned ones store a
    /// relative one, and this is the single place that difference is handled — see the interface.
    /// </summary>
    public string ResolveRepositoryPath(string localPath) =>
        Path.IsPathRooted(localPath) ? localPath : Path.Combine(_localReposPath, localPath);

    public Task<bool> IsRepositoryValidAsync(string repositoryPath) =>
        Task.FromResult(Repository.IsValid(repositoryPath));

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

    /// <summary>
    /// Counts a commit's lines by extension. Reads directly from the git object store; no
    /// working-tree checkout is needed.
    /// </summary>
    public async Task<Dictionary<string, int>> CountLinesInCommitAsync(string fullRepoPath, string commitSha, IEnumerable<string> fileExtensionsToCount)
    {
        if (!Repository.IsValid(fullRepoPath))
        {
            _logger.LogError("Local repository not found or invalid at {RepoPath}. Cannot count lines.", fullRepoPath);
            return new Dictionary<string, int>();
        }

        // Hoisted into a set ONCE per commit. `fileExtensionsToCount` arrives as an
        // IEnumerable<string> and was tested with .Contains() against every blob in the tree —
        // a linear scan per file per commit, over a list the caller re-enumerates each time.
        //
        // OrdinalIgnoreCase is a fix, not a tidy-up: the lookup key is produced by
        // Path.GetExtension(name.ToLowerInvariant()) while the configured extensions come from
        // user preferences verbatim, so a preference saved as ".CS" silently matched nothing under
        // the default comparer.
        var extensionsToCount = new HashSet<string>(fileExtensionsToCount, StringComparer.OrdinalIgnoreCase);
        InvalidateCachesIfExtensionsChanged(extensionsToCount);

        using var repo = _gitClient.OpenRepository(fullRepoPath);

        var commit = repo.Lookup<Commit>(commitSha);
        if (commit == null)
        {
            _logger.LogWarning("Commit {CommitSha} not found in repository at {RepoPath}", commitSha, fullRepoPath);
            return new Dictionary<string, int>();
        }

        if (commit.Tree == null)
        {
            _logger.LogWarning("Commit {CommitSha} has a null tree. Skipping line counting.", commitSha);
            return new Dictionary<string, int>();
        }

        // A copy, because the walk returns cached dictionaries that must not be handed out for the
        // caller to mutate — the root tree of an unchanged commit returns the very instance held
        // in the cache.
        var counts = new Dictionary<string, int>(await CountTreeAsync(commit.Tree, string.Empty, extensionsToCount));

        _logger.LogDebug("Counted commit {CommitSha}: {LineCounts}", commitSha, counts);
        return counts;
    }

    /// <summary>
    /// Cap on a single file's size before it is skipped by <see cref="EnumerateSourceFiles"/>.
    /// A source file this large is a generated bundle, a vendored blob or a data table — none of
    /// which says anything about how the code is written, and all of which would dominate any
    /// average they were included in.
    /// </summary>
    private const long MaxAnalysableFileBytes = 2 * 1024 * 1024;

    public IEnumerable<SourceFile> EnumerateSourceFiles(string repositoryPath, string commitSha, IEnumerable<string> fileExtensionsToCount)
    {
        if (!Repository.IsValid(repositoryPath))
        {
            _logger.LogError("Local repository not found or invalid at {RepoPath}. Cannot read source files.", repositoryPath);
            yield break;
        }

        var extensions = new HashSet<string>(fileExtensionsToCount, StringComparer.OrdinalIgnoreCase);

        using var repo = _gitClient.OpenRepository(repositoryPath);

        var commit = repo.Lookup<Commit>(commitSha);
        if (commit?.Tree is null)
        {
            _logger.LogWarning("Commit {CommitSha} not found (or has no tree) at {RepoPath}", commitSha, repositoryPath);
            yield break;
        }

        foreach (var file in WalkSourceFiles(commit.Tree, string.Empty, extensions))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Depth-first walk yielding counted, non-ignored blobs. Applies exactly the ignore rules the
    /// line counter applies — a directory pruned from the totals must not turn up in the health
    /// report, or the two would describe different codebases under the same repository name.
    /// </summary>
    private IEnumerable<SourceFile> WalkSourceFiles(Tree tree, string currentPath, HashSet<string> extensions)
    {
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(currentPath) ? entry.Name : $"{currentPath}/{entry.Name}";

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                var subTree = entry.Target as Tree;

                if (subTree is null
                        ? _fileIgnoreFilter.ShouldIgnoreDirectory(entryPath)
                        : _fileIgnoreFilter.ShouldIgnoreDirectory(entryPath, subTree.Select(e => e.Name)))
                {
                    continue;
                }

                if (subTree is null) continue;

                foreach (var file in WalkSourceFiles(subTree, entryPath, extensions))
                {
                    yield return file;
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                if (_fileIgnoreFilter.ShouldIgnoreFile(entry.Name, entryPath)) continue;

                var extension = Path.GetExtension(entry.Name.ToLowerInvariant());
                if (!extensions.Contains(extension)) continue;

                if (entry.Target is not Blob blob) continue;
                if (blob.Size > MaxAnalysableFileBytes)
                {
                    _logger.LogDebug("Skipping {Path} for analysis: {Size} bytes exceeds the cap", entryPath, blob.Size);
                    continue;
                }

                // Binary content reaches here only if it carries a counted source extension, which
                // in practice it does not. GetContentText decodes as UTF-8 with replacement rather
                // than throwing, so a stray byte costs one garbled character, not the report.
                yield return new SourceFile(entryPath, extension, blob.GetContentText());
            }
        }
    }

    /// <summary>
    /// Line counts for one tree, memoised on the tree's own object id.
    ///
    /// <para><b>Why this is memoised rather than merely tidy.</b> Analysis replays a repository's
    /// entire history, and the previous implementation walked and decompressed EVERY blob of EVERY
    /// commit — so a 4,000-commit repository counted the same unchanged files four thousand times,
    /// and analysis time grew with commits × repository size rather than with the amount of code
    /// that actually changed.</para>
    ///
    /// <para>Git trees are content-addressed: two trees with the same object id have byte-identical
    /// contents, transitively. So an unchanged directory between two commits is one dictionary
    /// lookup instead of a full recursive walk, and a commit that touched nothing (or whose root
    /// tree is unchanged) costs a single lookup. Only the path from the root down to genuinely
    /// changed blobs is ever re-walked.</para>
    ///
    /// <para>The key includes the PATH as well as the object id. The ignore filter's decisions
    /// depend on where a directory sits, not just what it holds, so the identical subtree appearing
    /// at two paths is legitimately two different answers.</para>
    /// </summary>
    private async Task<Dictionary<string, int>> CountTreeAsync(Tree tree, string currentPath, HashSet<string> fileExtensionsToCount)
    {
        var cacheKey = $"{tree.Sha} {currentPath}";
        if (_treeLineCounts.TryGetValue(cacheKey, out var memoised))
        {
            return memoised;
        }

        var counts = new Dictionary<string, int>();

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

                if (subTree is null) continue;

                foreach (var (extension, lines) in await CountTreeAsync(subTree, entryPath, fileExtensionsToCount))
                {
                    counts[extension] = counts.GetValueOrDefault(extension) + lines;
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                if (_fileIgnoreFilter.ShouldIgnoreFile(entry.Name, entryPath))
                {
                    continue;
                }

                var fileExtension = Path.GetExtension(entry.Name.ToLowerInvariant());
                if (!fileExtensionsToCount.Contains(fileExtension)) continue;

                if (entry.Target is not Blob blob)
                {
                    _logger.LogWarning("Tree entry {EntryName} is not a blob, or blob is null.", entry.Name);
                    continue;
                }

                counts[fileExtension] = counts.GetValueOrDefault(fileExtension) + await CountBlobLinesAsync(blob, fileExtension);
            }
        }

        Memoise(_treeLineCounts, cacheKey, counts);
        return counts;
    }

    /// <summary>
    /// Counts a single blob's lines with the strategy registered for its extension, falling back
    /// to the "*" strategy for anything without a dedicated one.
    ///
    /// <para>Memoised on the blob's object id AND its extension. The id alone is not enough:
    /// identical content stored as <c>.ts</c> and as <c>.css</c> is counted under different
    /// comment rules and legitimately yields different numbers. The subtree memo above already
    /// removes most of these calls; this one catches a file that merely MOVED, whose containing
    /// trees all changed while the blob did not.</para>
    /// </summary>
    private async Task<int> CountBlobLinesAsync(Blob blob, string fileExtension)
    {
        var cacheKey = $"{blob.Sha} {fileExtension}";
        if (_blobLineCounts.TryGetValue(cacheKey, out var memoised))
        {
            return memoised;
        }

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
        var lines = await lineCounter.CountLinesAsync(contentStream);

        Memoise(_blobLineCounts, cacheKey, lines);
        return lines;
    }

    /// <summary>
    /// Adds to a memo, clearing it wholesale once it grows past <see cref="MaxMemoEntries"/>.
    ///
    /// <para>Cleared rather than evicted one entry at a time. History is replayed oldest-first and
    /// adjacent commits share nearly all their content, so a fresh memo re-fills from the commits
    /// being walked right now within a few iterations — where an LRU would cost bookkeeping on
    /// every hit to protect entries that are about to age out anyway. The cap exists so a bulk
    /// import of large repositories cannot grow this without limit; it is not expected to be hit
    /// on a normal analysis.</para>
    /// </summary>
    private void Memoise<T>(Dictionary<string, T> memo, string key, T value)
    {
        if (memo.Count >= MaxMemoEntries)
        {
            _logger.LogDebug("Line-count memo reached {Cap} entries — clearing", MaxMemoEntries);
            memo.Clear();
        }

        memo[key] = value;
    }

    /// <summary>
    /// Drops both memos when the set of counted extensions changes.
    ///
    /// <para>The memos hold counts computed under one extension set, and this service is scoped —
    /// one instance serves every repository in a bulk import. Two users' preferences never meet
    /// here (a scope belongs to one caller), but a re-analysis triggered after a settings change
    /// legitimately asks for different numbers over the same trees, and returning the previous
    /// answer would make the new setting appear to have done nothing.</para>
    /// </summary>
    private void InvalidateCachesIfExtensionsChanged(HashSet<string> extensionsToCount)
    {
        var signature = string.Join(',', extensionsToCount.OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
        if (signature == _memoExtensionSignature) return;

        _treeLineCounts.Clear();
        _blobLineCounts.Clear();
        _memoExtensionSignature = signature;
    }

    public async Task<IEnumerable<CommitStatsDto>> GetCommitStatsAsync(string repositoryPath, DateTime? sinceDate = null)
    {
        // Off the request thread: the walk below is synchronous LibGit2Sharp work over the whole
        // history, and it is called from a background analysis job that must not block on it.
        return await Task.Run(() => GetCommitStatsCore(repositoryPath, sinceDate));
    }

    /// <summary>Synchronous history walk, kept separate so the public method owns the threading.</summary>
    private IEnumerable<CommitStatsDto> GetCommitStatsCore(string fullRepoPath, DateTime? sinceDate)
    {
        _logger.LogInformation("Getting commit stats for repository at {RepoPath} since {SinceDate}", fullRepoPath, sinceDate);

        if (!Repository.IsValid(fullRepoPath))
        {
            _logger.LogError("Local repository not found or invalid at {RepoPath}. Cannot get commit stats.", fullRepoPath);
            return Enumerable.Empty<CommitStatsDto>();
        }

        var commitStatsList = new List<CommitStatsDto>();

        using (var repo = _gitClient.OpenRepository(fullRepoPath))
        {
            var filter = new CommitFilter
            {
                SortBy = CommitSortStrategies.Time,
                IncludeReachableFrom = repo.Head
            };

            IEnumerable<Commit> commits = repo.Commits.QueryBy(filter);

            if (sinceDate.HasValue)
            {
                // Convert sinceDate to UTC for comparison with commit dates
                var sinceDateUtc = sinceDate.Value.Kind == DateTimeKind.Utc ? sinceDate.Value : sinceDate.Value.ToUniversalTime();
                commits = commits.Where(c => c.Author.When.UtcDateTime >= sinceDateUtc);
            }

            var commitsList = commits.ToList();
            _logger.LogInformation("Processing {CommitCount} commits after date filtering", commitsList.Count);

            foreach (var commit in commitsList)
            {
                int linesAdded;
                int linesRemoved;

                if (commit.Parents.Any())
                {
                    var patch = repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree);
                    linesAdded = patch.LinesAdded;
                    linesRemoved = patch.LinesDeleted;
                }
                else
                {
                    // Initial commit, count all lines as added
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
        _logger.LogInformation("Found {CommitCount} commit stats for repository at {RepoPath}", commitStatsList.Count, fullRepoPath);
        return commitStatsList;
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
