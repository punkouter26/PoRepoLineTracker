namespace PoRepoLineTracker.API.Features.Repositories;

/// <summary>Removes every repository a user tracks: table rows, clones on disk, snapshots.</summary>
public record RemoveAllRepositoriesCommand(UserId UserId);

public class RemoveAllRepositoriesCommandHandler(
    IRepositoryDataService repositoryDataService,
    IGitHubService gitHubService,
    ICodeHealthSnapshotStore snapshotStore,
    ILogger<RemoveAllRepositoriesCommandHandler> logger)
{
    public async Task Handle(RemoveAllRepositoriesCommand request, CancellationToken cancellationToken = default)
    {
        // Read the ids first: once the rows are gone there is nothing left to say which clone
        // directories were this user's.
        var repositories = (await repositoryDataService.GetAllRepositoriesAsync(request.UserId)).ToList();

        await repositoryDataService.RemoveAllRepositoriesAsync(request.UserId);

        // Only this user's clones. This used to delete the configured LocalReposPath root, which
        // is shared by every user of the deployment.
        foreach (var repository in repositories)
        {
            await DeleteRepositoryCommandHandler.DeleteDerivedDataAsync(
                repository.Id, gitHubService, snapshotStore, logger);
        }

        logger.LogInformation("Removed {Count} repositories and their data for user {UserId}.",
            repositories.Count, request.UserId);
    }
}
