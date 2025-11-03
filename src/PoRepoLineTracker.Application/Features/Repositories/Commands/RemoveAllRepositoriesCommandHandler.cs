using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

/// <summary>
/// Command Pattern: Handler for RemoveAllRepositoriesCommand.
/// Implements comprehensive cleanup of all repository data including Azure Table Storage and local file system.
/// Uses Repository Pattern via IRepositoryDataService for storage operations.
/// </summary>
public class RemoveAllRepositoriesCommandHandler : IRequestHandler<RemoveAllRepositoriesCommand, Unit>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RemoveAllRepositoriesCommandHandler> _logger;

    public RemoveAllRepositoriesCommandHandler(
        IRepositoryDataService repositoryDataService,
        IConfiguration configuration,
        ILogger<RemoveAllRepositoriesCommandHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Unit> Handle(RemoveAllRepositoriesCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting removal of all repositories and associated data.");

        try
        {
            // Step 1: Remove all data from Azure Table Storage
            await _repositoryDataService.RemoveAllRepositoriesAsync();
            _logger.LogInformation("All repository data removed from Azure Table Storage successfully.");

            // Step 2: Remove all local repository directories
            await RemoveAllLocalRepositoriesAsync();
            _logger.LogInformation("All local repository directories removed successfully.");

            _logger.LogInformation("Successfully completed removal of all repositories and associated data.");
            return Unit.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while removing all repositories: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Strategy Pattern: Implements file system cleanup strategy.
    /// Removes the entire local repositories directory tree.
    /// Uses garbage collection and retries to handle locked Git repository files.
    /// </summary>
    private async Task RemoveAllLocalRepositoriesAsync()
    {
        var localReposPath = _configuration["GitHub:LocalReposPath"];

        if (string.IsNullOrEmpty(localReposPath))
        {
            _logger.LogWarning("GitHub:LocalReposPath not configured. Skipping local repository cleanup.");
            return;
        }

        if (!Directory.Exists(localReposPath))
        {
            _logger.LogInformation("Local repositories directory does not exist: {Path}. No cleanup needed.", localReposPath);
            return;
        }

        _logger.LogInformation("Removing local repositories directory: {Path}", localReposPath);

        await Task.Run(() =>
        {
            // Force garbage collection to release any open file handles
            // This is particularly important for LibGit2Sharp Repository objects
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Retry logic to handle file locks
            const int maxRetries = 3;
            const int delayBetweenRetriesMs = 500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation("Attempting to delete local repositories directory (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);

                    // Force delete the entire directory tree
                    Directory.Delete(localReposPath, recursive: true);
                    _logger.LogInformation("Local repositories directory removed successfully: {Path}", localReposPath);
                    return; // Success - exit
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning(ex, "Attempt {Attempt} failed. Some files may be locked. Retrying in {DelayMs}ms...", attempt, delayBetweenRetriesMs);
                    Thread.Sleep(delayBetweenRetriesMs);

                    // Force another GC cycle between retries
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch (IOException ex) when (attempt == maxRetries)
                {
                    _logger.LogWarning(ex, "All retry attempts exhausted. Attempting individual file cleanup for: {Path}", localReposPath);

                    // Attempt to delete individual files if directory deletion fails
                    ForceDeleteDirectory(localReposPath);
                    return;
                }
            }
        });
    }

    /// <summary>
    /// Strategy Pattern: Implements aggressive file deletion strategy for locked files.
    /// Uses recursive approach to handle file system locks and read-only attributes.
    /// Includes retry logic and garbage collection to release file handles.
    /// </summary>
    private void ForceDeleteDirectory(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return;

            _logger.LogInformation("Force deleting directory: {Path}", directoryPath);

            // Force GC again before individual file deletion
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Remove read-only attributes and delete files
            foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    
                    // Retry file deletion
                    for (int retry = 0; retry < 3; retry++)
                    {
                        try
                        {
                            File.Delete(file);
                            break; // Success
                        }
                        catch (IOException) when (retry < 2)
                        {
                            Thread.Sleep(100);
                            GC.Collect();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete file: {FilePath}", file);
                }
            }

            // Delete directories bottom-up
            var directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length); // Delete deepest first

            foreach (var directory in directories)
            {
                try
                {
                    Directory.Delete(directory, false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete directory: {DirectoryPath}", directory);
                }
            }

            // Finally delete the root directory
            try
            {
                Directory.Delete(directoryPath, false);
                _logger.LogInformation("Successfully force-deleted directory: {Path}", directoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete root directory: {DirectoryPath}", directoryPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force delete directory: {DirectoryPath}", directoryPath);
            throw;
        }
    }
}
