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
        // Determine the base path for local repositories.
        // In Azure App Service, use a path within the ephemeral storage.
        // Locally, use the configured path or a default "LocalRepos" directory.
        var homePath = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(homePath))
        {
            // Running in Azure App Service
            _localReposPath = Path.Combine(homePath, "site", "wwwroot", "temp_repos");
        }
        else
        {
            // Running locally
            _localReposPath = configuration["GitHub:LocalReposPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "LocalRepos");
        }
        _gitHubPat = configuration["GitHub:PAT"];

        if (!Directory.Exists(_localReposPath))
        {
            Directory.CreateDirectory(_localReposPath);
        }

        // HttpClient is now pre-configured with PAT in Program.cs via named client
        // _gitHubPat is still needed for git operations (cloning)
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
        // Define ignore patterns for categories 1,2,3,4,5,6,8,10 only
        
        // Category 1: Package Manager Files
        var packageManagerFiles = new[]
        {
            "packages.config", "package-lock.json", "yarn.lock", "paket.lock", "paket.dependencies"
        };
        
        // Category 2: Build Output & Compiled Files  
        var buildOutputExtensions = new[]
        {
            ".dll", ".exe", ".pdb", ".obj", ".cache", ".lib", ".exp", ".ilk", ".idb", ".nupkg"
        };
        
        // Category 3: Auto-Generated Code Files
        var autoGeneratedExtensions = new[]
        {
            ".designer.cs", ".g.cs", ".g.i.cs", ".designer.vb", ".g.vb"
        };
        
        var autoGeneratedFilePatterns = new[]
        {
            "reference.cs", "temporarygeneratedfile"
        };
        
        // Category 4: Database Migration Files (in Migrations/ folder)
        var migrationFolderPattern = "migrations/";
        
        // Category 5: Third-Party JavaScript Libraries
        var thirdPartyJsExtensions = new[]
        {
            ".min.js", ".min.css"
        };
        
        var thirdPartyJsPatterns = new[]
        {
            "jquery", "bootstrap"
        };
        
        // Category 6: IDE and Tool Configuration
        var ideConfigExtensions = new[]
        {
            ".user", ".suo", ".vspscc", ".vssscc"
        };
        
        var ideConfigFiles = new[]
        {
            "launchsettings.json"
        };
        
        // Category 8: Web Assets from Package Managers
        var webAssetExtensions = new[]
        {
            ".woff", ".woff2", ".ttf", ".eot", ".otf"
        };
        
        // Category 10: Test Data and Mock Files
        var testDataExtensions = new[]
        {
            ".resx", ".settings", ".resources"
        };

        var directoryPatternsToIgnore = new[]
        {
            // Category 2: Build Output directories
            "bin/", "obj/", "debug/", "release/",
            // Category 5: Third-party JS directories  
            "node_modules/", "bower_components/", "jspm_packages/", "typings/",
            // Category 6: IDE directories
            ".vs/", ".vscode/", ".idea/", 
            // Category 8: Web asset directories
            "wwwroot/lib/",
            // Always ignore .git
            ".git/",
            // Category 1: Package directories
            "packages/"
        };

        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(currentPath) ? entry.Name : $"{currentPath}/{entry.Name}";
            
            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // Check if this directory should be ignored
                var normalizedPath = entryPath.Replace("\\", "/").ToLowerInvariant() + "/";
                bool shouldIgnoreDirectory = directoryPatternsToIgnore.Any(pattern => 
                    normalizedPath.EndsWith(pattern) || normalizedPath.Contains("/" + pattern) || normalizedPath.StartsWith(pattern));

                if (shouldIgnoreDirectory)
                {
                    _logger.LogInformation("Ignoring directory: {DirectoryName} (matches ignore pattern)", entryPath);
                    continue;
                }

                // Recursively process subdirectories
                _logger.LogDebug("Traversing directory: {DirectoryName}", entryPath);
                if (entry.Target is Tree subTree)
                {
                    await ProcessTreeEntry(subTree, fileExtensionsToCount, lineCounts, entryPath);
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var fileName = entry.Name.ToLowerInvariant();
                var fileExtension = Path.GetExtension(fileName);
                
                // Category 1: Check package manager files by exact name
                if (packageManagerFiles.Any(name => fileName.Equals(name.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring package manager file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 2: Check build output extensions
                if (buildOutputExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring build output file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 3: Check auto-generated file extensions
                if (autoGeneratedExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring auto-generated file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 3: Check auto-generated file patterns
                if (autoGeneratedFilePatterns.Any(pattern => fileName.Contains(pattern.ToLowerInvariant())) ||
                    fileName.Contains("assemblyinfo"))
                {
                    _logger.LogDebug("Ignoring auto-generated file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 4: Check database migration files
                if (entryPath.ToLowerInvariant().Contains(migrationFolderPattern))
                {
                    _logger.LogDebug("Ignoring migration file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 5: Check third-party JS extensions
                if (thirdPartyJsExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring third-party JS file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 5: Check third-party JS patterns
                if (thirdPartyJsPatterns.Any(pattern => fileName.Contains(pattern.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring third-party JS library file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 6: Check IDE config extensions
                if (ideConfigExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring IDE config file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 6: Check IDE config files by name
                if (ideConfigFiles.Any(name => fileName.Equals(name.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring IDE config file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 8: Check web asset extensions
                if (webAssetExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring web asset file: {FileName}", entry.Name);
                    continue;
                }
                
                // Category 10: Check test data extensions
                if (testDataExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    _logger.LogDebug("Ignoring test data file: {FileName}", entry.Name);
                    continue;
                }

                // Now check if it's in our allowed extensions
                if (fileExtensionsToCount.Contains(fileExtension))
                {
                    var blob = entry.Target as Blob;
                    if (blob != null)
                    {
                        _logger.LogDebug("Processing file: {FileName}, Size: {FileSize} bytes", entry.Name, blob.Size);
                        using (var contentStream = blob.GetContentStream())
                        using (var reader = new StreamReader(contentStream))
                        {
                            int lines = 0;
                            string? line;
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                lines++;
                            }
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
                totalLines = await CountLinesInTreeAsync(headCommit.Tree, fileExtensionsToCount, "");
            }
            _logger.LogInformation("Total lines of code for {LocalPath}: {TotalLines}", fullLocalPath, totalLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting total lines of code for {LocalPath}: {ErrorMessage}", fullLocalPath, ex.Message);
        }

        return totalLines;
    }

    private async Task<long> CountLinesInTreeAsync(Tree tree, IEnumerable<string> fileExtensionsToCount, string currentPath = "")
    {
        // Define ignore patterns for categories 1,2,3,4,5,6,8,10 only (same as ProcessTreeEntry)
        
        // Category 1: Package Manager Files
        var packageManagerFiles = new[]
        {
            "packages.config", "package-lock.json", "yarn.lock", "paket.lock", "paket.dependencies"
        };
        
        // Category 2: Build Output & Compiled Files  
        var buildOutputExtensions = new[]
        {
            ".dll", ".exe", ".pdb", ".obj", ".cache", ".lib", ".exp", ".ilk", ".idb", ".nupkg"
        };
        
        // Category 3: Auto-Generated Code Files
        var autoGeneratedExtensions = new[]
        {
            ".designer.cs", ".g.cs", ".g.i.cs", ".designer.vb", ".g.vb"
        };
        
        var autoGeneratedFilePatterns = new[]
        {
            "reference.cs", "temporarygeneratedfile"
        };
        
        // Category 4: Database Migration Files (in Migrations/ folder)
        var migrationFolderPattern = "migrations/";
        
        // Category 5: Third-Party JavaScript Libraries
        var thirdPartyJsExtensions = new[]
        {
            ".min.js", ".min.css"
        };
        
        var thirdPartyJsPatterns = new[]
        {
            "jquery", "bootstrap"
        };
        
        // Category 6: IDE and Tool Configuration
        var ideConfigExtensions = new[]
        {
            ".user", ".suo", ".vspscc", ".vssscc"
        };
        
        var ideConfigFiles = new[]
        {
            "launchsettings.json"
        };
        
        // Category 8: Web Assets from Package Managers
        var webAssetExtensions = new[]
        {
            ".woff", ".woff2", ".ttf", ".eot", ".otf"
        };
        
        // Category 10: Test Data and Mock Files
        var testDataExtensions = new[]
        {
            ".resx", ".settings", ".resources"
        };

        var directoryPatternsToIgnore = new[]
        {
            // Category 2: Build Output directories
            "bin/", "obj/", "debug/", "release/",
            // Category 5: Third-party JS directories  
            "node_modules/", "bower_components/", "jspm_packages/", "typings/",
            // Category 6: IDE directories
            ".vs/", ".vscode/", ".idea/", 
            // Category 8: Web asset directories
            "wwwroot/lib/",
            // Always ignore .git
            ".git/",
            // Category 1: Package directories
            "packages/"
        };

        long lines = 0;
        foreach (var entry in tree)
        {
            var entryPath = string.IsNullOrEmpty(currentPath) ? entry.Name : $"{currentPath}/{entry.Name}";

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // Check if this directory should be ignored
                var normalizedPath = entryPath.Replace("\\", "/").ToLowerInvariant() + "/";
                bool shouldIgnoreDirectory = directoryPatternsToIgnore.Any(pattern => 
                    normalizedPath.EndsWith(pattern) || normalizedPath.Contains("/" + pattern) || normalizedPath.StartsWith(pattern));

                if (!shouldIgnoreDirectory)
                {
                    if (entry.Target is Tree subTree)
                    {
                        lines += await CountLinesInTreeAsync(subTree, fileExtensionsToCount, entryPath);
                    }
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var fileName = entry.Name.ToLowerInvariant();
                var fileExtension = Path.GetExtension(fileName);
                
                // Category 1: Check package manager files by exact name
                if (packageManagerFiles.Any(name => fileName.Equals(name.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 2: Check build output extensions
                if (buildOutputExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 3: Check auto-generated file extensions
                if (autoGeneratedExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 3: Check auto-generated file patterns
                if (autoGeneratedFilePatterns.Any(pattern => fileName.Contains(pattern.ToLowerInvariant())) ||
                    fileName.Contains("assemblyinfo"))
                {
                    continue; // Skip ignored files
                }
                
                // Category 4: Check database migration files
                if (entryPath.ToLowerInvariant().Contains(migrationFolderPattern))
                {
                    continue; // Skip ignored files
                }
                
                // Category 5: Check third-party JS extensions
                if (thirdPartyJsExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 5: Check third-party JS patterns
                if (thirdPartyJsPatterns.Any(pattern => fileName.Contains(pattern.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 6: Check IDE config extensions
                if (ideConfigExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 6: Check IDE config files by name
                if (ideConfigFiles.Any(name => fileName.Equals(name.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 8: Check web asset extensions
                if (webAssetExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }
                
                // Category 10: Check test data extensions
                if (testDataExtensions.Any(ext => fileName.EndsWith(ext.ToLowerInvariant())))
                {
                    continue; // Skip ignored files
                }

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

    public async Task<IEnumerable<GitHubUserRepository>> GetUserRepositoriesAsync()
    {
        _logger.LogInformation("Fetching user repositories from GitHub API.");
        
        if (string.IsNullOrEmpty(_gitHubPat))
        {
            _logger.LogError("GitHub PAT is not configured. Cannot fetch user repositories.");
            throw new InvalidOperationException("GitHub Personal Access Token is not configured.");
        }

        try
        {
            // GitHub API endpoint to get authenticated user's repositories
            var response = await _httpClient.GetAsync("https://api.github.com/user/repos?type=owner&sort=name&direction=asc&per_page=100");
            response.EnsureSuccessStatusCode();
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            var repoData = System.Text.Json.JsonSerializer.Deserialize<List<GitHubApiRepository>>(jsonContent);
            
            var userRepositories = repoData?.Select(repo => new GitHubUserRepository
            {
                Name = repo.name ?? string.Empty,
                FullName = repo.full_name ?? string.Empty,
                CloneUrl = repo.clone_url ?? string.Empty,
                Description = repo.description ?? string.Empty,
                IsPrivate = repo.@private,
                Language = repo.language ?? string.Empty
            }) ?? Enumerable.Empty<GitHubUserRepository>();
            
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
