using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Serilog;
using System.IO.Compression;
using System.Diagnostics;

namespace PoRepoLineTracker.API.Features.Repositories;

internal static class UploadEndpoints
{
    internal static void MapUploadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var uploads = endpoints.MapGroup("/api/repositories")
            .WithTags("Uploads")
            .RequireAuthorization();

        // Upload a zipped repository (.git folder + code) for analysis
        // DisableRequestSizeLimit: overrides the 30 MB Kestrel default for this endpoint only
        uploads.MapPost("/upload-zip", async (HttpContext ctx, IMediator mediator, IServiceScopeFactory scopeFactory, IConfiguration configuration) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var serverRequestId = ctx.TraceIdentifier;
            var clientRequestId = ctx.Request.Headers["X-Upload-Client-Request-Id"].FirstOrDefault() ?? "not-provided";
            var endpointTimer = Stopwatch.StartNew();

            var uploadLog = Log.ForContext("ServerRequestId", serverRequestId)
                .ForContext("ClientUploadRequestId", clientRequestId)
                .ForContext("UploadUserId", userId);

            uploadLog.Information(
                "ZIP upload request started. ContentLength={ContentLength} ContentType={ContentType} Path={Path}",
                ctx.Request.ContentLength,
                ctx.Request.ContentType,
                ctx.Request.Path);

            // Disable the per-request size limit so large ZIPs are not rejected by Kestrel
            var bodySizeFeature = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodySizeFeature is { IsReadOnly: false })
            {
                bodySizeFeature.MaxRequestBodySize = null;
                uploadLog.Information("Disabled Kestrel MaxRequestBodySize for this upload request.");
            }
            else
            {
                uploadLog.Warning("Could not disable Kestrel MaxRequestBodySize: feature missing or read-only.");
            }

            try
            {
                var formReadTimer = Stopwatch.StartNew();
                var form = await ctx.Request.ReadFormAsync(
                    new Microsoft.AspNetCore.Http.Features.FormOptions
                    {
                        MultipartBodyLengthLimit = 600 * 1024 * 1024, // 600 MB
                        ValueLengthLimit = int.MaxValue,
                        MultipartHeadersLengthLimit = int.MaxValue
                    });
                formReadTimer.Stop();

                uploadLog.Information(
                    "Multipart form parsed in {ElapsedMs} ms. Files={FileCount} Fields={FieldCount}",
                    formReadTimer.ElapsedMilliseconds,
                    form.Files.Count,
                    form.Keys.Count);

                var file = form.Files.FirstOrDefault();

                if (file == null || file.Length == 0)
                {
                    uploadLog.Warning("Upload rejected: no file sent or file length is zero.");
                    return Results.BadRequest(new UploadError { Error = "No file uploaded or file is empty." });
                }

                if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    uploadLog.Warning("Upload rejected: non-zip file. FileName={FileName} Length={Length}", file.FileName, file.Length);
                    return Results.BadRequest(new UploadError { Error = "Only .zip files are accepted." });
                }

                var repoName = form["repoName"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(repoName))
                {
                    // Extract repo name from zip filename
                    var zipName = Path.GetFileNameWithoutExtension(file.FileName);
                    repoName = SanitizeRepoName(zipName);
                }
                else
                {
                    repoName = SanitizeRepoName(repoName);
                }

                uploadLog.Information(
                    "Processing ZIP upload. RepositoryName={RepoName} FileName={FileName} SizeBytes={Size}",
                    repoName,
                    file.FileName,
                    file.Length);

                // Create a unique temp directory for extraction
                var tempPath = Path.Combine(Path.GetTempPath(), $"repo_upload_{Guid.NewGuid()}");
                uploadLog.Information("Using temp extraction path {TempPath}", tempPath);

                try
                {
                    // Extract zip to temp directory
                    Directory.CreateDirectory(tempPath);

                    var extractTimer = Stopwatch.StartNew();
                    int entryCount;
                    long estimatedUncompressedBytes;

                    using (var zipStream = file.OpenReadStream())
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    {
                        entryCount = archive.Entries.Count;
                        estimatedUncompressedBytes = archive.Entries.Sum(e => e.Length);
                        uploadLog.Information(
                            "ZIP opened. EntryCount={EntryCount} EstimatedUncompressedBytes={EstimatedUncompressedBytes}",
                            entryCount,
                            estimatedUncompressedBytes);

                        archive.ExtractToDirectory(tempPath);
                    }

                    extractTimer.Stop();
                    var extractedDirCount = Directory.EnumerateDirectories(tempPath, "*", SearchOption.AllDirectories).Count();
                    var extractedFileCount = Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories).Count();
                    uploadLog.Information(
                        "ZIP extracted in {ElapsedMs} ms. ExtractedDirectories={DirCount} ExtractedFiles={FileCount}",
                        extractTimer.ElapsedMilliseconds,
                        extractedDirCount,
                        extractedFileCount);

                    // Find the .git folder (could be at root or in a subdirectory)
                    var (gitPath, repoRoot) = FindGitFolder(tempPath);

                    if (string.IsNullOrEmpty(gitPath))
                    {
                        var immediateDirs = Directory.GetDirectories(tempPath).Select(Path.GetFileName).ToArray();
                        uploadLog.Warning(
                            "No .git folder found in uploaded zip for repository '{RepoName}'. TopLevelDirectories={TopLevelDirectories}",
                            repoName,
                            immediateDirs);
                        return Results.BadRequest(new UploadError { Error = "The uploaded zip does not contain a .git folder. Please upload a zip containing a complete repository with its .git folder." });
                    }

                    uploadLog.Information("Detected repository root. RepoRoot={RepoRoot} GitPath={GitPath}", repoRoot, gitPath);

                    // Determine the base path for local repositories (same as GitHubService)
                    var homePath = Environment.GetEnvironmentVariable("HOME");
                    string localReposBasePath;
                    if (!string.IsNullOrEmpty(homePath))
                    {
                        localReposBasePath = Path.Combine(homePath, "site", "wwwroot", "temp_repos");
                    }
                    else
                    {
                        localReposBasePath = configuration[ConfigKeys.GitHub.LocalReposPath] ?? Path.Combine(Directory.GetCurrentDirectory(), "LocalRepos");
                    }

                    if (!Directory.Exists(localReposBasePath))
                    {
                        Directory.CreateDirectory(localReposBasePath);
                    }

                    // Create a unique permanent path for this repo
                    var repoId = Guid.NewGuid();
                    var permanentPath = Path.Combine(localReposBasePath, $"local_{repoId}");

                    // Move the extracted content to the permanent location
                    if (repoRoot != tempPath)
                    {
                        // Move contents from subdirectory to permanent path
                        Directory.CreateDirectory(permanentPath);
                        MoveDirectoryContent(repoRoot, permanentPath);
                    }
                    else
                    {
                        // Already at root, just move to permanent location
                        Directory.Move(tempPath, permanentPath);
                        gitPath = Path.Combine(permanentPath, ".git");
                    }

                    uploadLog.Information("Moved extracted repository to permanent path {PermanentPath}", permanentPath);

                    // Create a repository entry - use permanentPath as LocalPath (repo root, not .git folder)
                    var command = new AddLocalRepositoryCommand
                    {
                        UserId = userId,
                        Name = repoName,
                        Owner = "Local Upload",
                        LocalGitPath = permanentPath
                    };

                    var repository = await mediator.Send(command);

                    // Update repository with the permanent local path for consistency
                    repository.LocalPath = permanentPath;
                    var repositoryDataService = ctx.RequestServices.GetService(typeof(IRepositoryDataService)) as IRepositoryDataService;
                    await repositoryDataService!.UpdateRepositoryAsync(repository);
                    uploadLog.Information("Repository metadata updated. RepoId={RepoId} LocalPath={LocalPath}", repository.Id, repository.LocalPath);

                    // Queue background analysis
                    _ = Task.Run(async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var bgMediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                        try
                        {
                            Log.Information(
                                "Background analysis started for uploaded repository. RepoId={RepoId} ClientUploadRequestId={ClientUploadRequestId}",
                                repository.Id,
                                clientRequestId);
                            await bgMediator.Send(new AnalyzeRepositoryCommitsCommand(repository.Id, ForceReanalysis: false));
                            Log.Information(
                                "Background analysis completed for uploaded repository. RepoId={RepoId} ClientUploadRequestId={ClientUploadRequestId}",
                                repository.Id,
                                clientRequestId);
                        }
                        catch (Exception bgEx)
                        {
                            Log.Error(
                                bgEx,
                                "Background analysis failed for uploaded repository. RepoId={RepoId} ClientUploadRequestId={ClientUploadRequestId} Error={Message}",
                                repository.Id,
                                clientRequestId,
                                bgEx.Message);
                        }
                    });

                    endpointTimer.Stop();
                    uploadLog.Information(
                        "ZIP upload completed successfully in {ElapsedMs} ms. RepoName={RepoName} RepoId={RepoId}",
                        endpointTimer.ElapsedMilliseconds,
                        repoName,
                        repository.Id);

                    return Results.Created($"/api/repositories/{repository.Id}", new UploadResult
                    {
                        Id = repository.Id,
                        Name = repository.Name,
                        Owner = repository.Owner,
                        Message = "Repository uploaded successfully. Analysis is starting in the background."
                    });
                }
                finally
                {
                    // Clean up temp directory if it still exists
                    try
                    {
                        if (Directory.Exists(tempPath))
                        {
                            Directory.Delete(tempPath, recursive: true);
                            uploadLog.Debug("Cleaned temp directory {TempPath}", tempPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        uploadLog.Warning(ex, "Failed to clean up temp directory {TempPath}", tempPath);
                    }
                }
            }
            catch (BadHttpRequestException ex)
            {
                endpointTimer.Stop();
                uploadLog.Error(ex,
                    "Bad HTTP request during ZIP upload after {ElapsedMs} ms. ContentLength={ContentLength}. This often indicates request-size limits or malformed multipart payload.",
                    endpointTimer.ElapsedMilliseconds,
                    ctx.Request.ContentLength);

                return Results.Problem(
                    title: "ZIP upload request was rejected",
                    detail: ex.Message,
                    statusCode: (int)HttpStatusCode.RequestEntityTooLarge,
                    extensions: new Dictionary<string, object?>
                    {
                        ["serverRequestId"] = serverRequestId,
                        ["clientUploadRequestId"] = clientRequestId,
                        ["contentLength"] = ctx.Request.ContentLength
                    });
            }
            catch (InvalidDataException ex)
            {
                endpointTimer.Stop();
                uploadLog.Error(ex,
                    "Invalid multipart payload during ZIP upload after {ElapsedMs} ms.",
                    endpointTimer.ElapsedMilliseconds);

                return Results.Problem(
                    title: "Invalid upload payload",
                    detail: ex.Message,
                    statusCode: (int)HttpStatusCode.BadRequest,
                    extensions: new Dictionary<string, object?>
                    {
                        ["serverRequestId"] = serverRequestId,
                        ["clientUploadRequestId"] = clientRequestId
                    });
            }
            catch (Exception ex)
            {
                endpointTimer.Stop();
                uploadLog.Error(ex,
                    "Unhandled ZIP upload error after {ElapsedMs} ms.",
                    endpointTimer.ElapsedMilliseconds);

                return Results.Problem(
                    title: "Error processing zip upload",
                    detail: ex.Message,
                    statusCode: (int)HttpStatusCode.InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["serverRequestId"] = serverRequestId,
                        ["clientUploadRequestId"] = clientRequestId
                    });
            }
        })
        .WithName("UploadZipRepository");
    }

    /// <summary>
    /// Locates the .git directory inside an extracted upload. Internal rather than private so the
    /// Unit tier can assert on the three layouts a real zip arrives in — repository at the root,
    /// wrapped in a named folder (what GitHub's "Download ZIP" produces), or neither.
    /// </summary>
    internal static (string gitPath, string repoRoot) FindGitFolder(string basePath)
    {
        // Check at root level
        var rootGitPath = Path.Combine(basePath, ".git");
        if (Directory.Exists(rootGitPath) && File.Exists(Path.Combine(rootGitPath, "config")))
        {
            return (rootGitPath, basePath);
        }

        // Check in subdirectories (zip might have a single folder at root)
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var subGitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(subGitPath) && File.Exists(Path.Combine(subGitPath, "config")))
            {
                // Return the git path and the containing directory
                return (subGitPath, dir);
            }
        }

        // Also check if there's a single subdirectory with all content (common from git archive)
        var subDirs = Directory.GetDirectories(basePath);
        if (subDirs.Length == 1)
        {
            var singleSubDir = subDirs[0];
            var innerGitPath = Path.Combine(singleSubDir, ".git");
            if (Directory.Exists(innerGitPath) && File.Exists(Path.Combine(innerGitPath, "config")))
            {
                return (innerGitPath, singleSubDir);
            }
        }

        return (null!, null!);
    }

    private static void MoveDirectoryContent(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Move(file, destFile, overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(source))
        {
            var destSubDir = Path.Combine(dest, Path.GetFileName(subDir));
            MoveDirectoryContent(subDir, destSubDir);
        }
        // Try to clean up the now-empty source directory
        try
        {
            Directory.Delete(source, recursive: true);
        }
        catch { /* Ignore cleanup errors */ }
    }

    /// <summary>
    /// Reduces an arbitrary uploaded filename to a safe display name for the repository.
    ///
    /// <para>The result does NOT reach the filesystem today — the extracted tree lands in
    /// <c>local_{Guid}</c> and this value is only stored and shown. It is still reduced to a leaf
    /// name with no separators, because that is a cheap invariant to hold and the day someone does
    /// build a path from it is the day it would otherwise become a traversal. Internal so the Unit
    /// tier can pin that invariant.</para>
    /// </summary>
    internal static string SanitizeRepoName(string name)
    {
        // Both separators explicitly, NOT just Path.GetInvalidFileNameChars(). That set is
        // platform-dependent: on Windows it includes '\', on Linux it contains only '/' and '\0'.
        // This app is developed on Windows and DEPLOYS TO LINUX APP SERVICE, so relying on it
        // alone means "C:\Users\x\repo" comes back with its backslashes intact in production and
        // the leaf-name guarantee below silently does not hold there. Caught by the Unit tier on
        // a Linux CI runner, having passed locally.
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\']).Distinct().ToArray();

        var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        if (sanitized.Length > 100)
        {
            sanitized = sanitized[..100];
        }
        return string.IsNullOrWhiteSpace(sanitized) ? "uploaded-repo" : sanitized;
    }
}