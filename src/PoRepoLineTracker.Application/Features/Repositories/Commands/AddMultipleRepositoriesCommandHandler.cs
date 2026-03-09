using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public class AddMultipleRepositoriesCommandHandler : IRequestHandler<AddMultipleRepositoriesCommand, IEnumerable<GitHubRepository>>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<AddMultipleRepositoriesCommandHandler> _logger;

    public AddMultipleRepositoriesCommandHandler(
        IRepositoryDataService repositoryDataService,
        ILogger<AddMultipleRepositoriesCommandHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<GitHubRepository>> Handle(AddMultipleRepositoriesCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== START AddMultipleRepositoriesCommandHandler ===");
        _logger.LogInformation("Received request to add {Count} repositories", request.Repositories.Count());

        // Log each repository in the request
        var repoList = request.Repositories.ToList();
        for (int i = 0; i < repoList.Count; i++)
        {
            var repo = repoList[i];
            _logger.LogInformation("Repository [{Index}]: Owner='{Owner}', Name='{RepoName}', CloneUrl='{CloneUrl}'",
                i, repo.Owner ?? "NULL", repo.RepoName ?? "NULL", repo.CloneUrl ?? "NULL");
        }

        var addedRepositories = new List<GitHubRepository>();
        var repositoriesToAnalyze = new List<Guid>();

        // PHASE 1: Add repositories to database
        foreach (var repo in request.Repositories)
        {
            try
            {
                _logger.LogInformation("Processing repository: {Owner}/{Name}", repo.Owner, repo.RepoName);

                // Validate repository data
                if (string.IsNullOrWhiteSpace(repo.Owner))
                {
                    _logger.LogWarning("Skipping repository with empty Owner. RepoName={RepoName}", repo.RepoName ?? "NULL");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(repo.RepoName))
                {
                    _logger.LogWarning("Skipping repository with empty RepoName. Owner={Owner}", repo.Owner ?? "NULL");
                    continue;
                }

                // Check if repository already exists for this user
                _logger.LogInformation("Checking if repository {Owner}/{Name} already exists for user {UserId}...", repo.Owner, repo.RepoName, request.UserId);
                var existingRepo = await _repositoryDataService.GetRepositoryByOwnerAndNameAsync(repo.Owner, repo.RepoName, request.UserId);
                if (existingRepo != null)
                {
                    _logger.LogInformation("Repository {Owner}/{Name} already exists with ID {Id}, adding to result list.",
                        repo.Owner, repo.RepoName, existingRepo.Id);
                    addedRepositories.Add(existingRepo);
                    continue;
                }

                // Create new repository
                _logger.LogInformation("Creating new repository entity for {Owner}/{Name}", repo.Owner, repo.RepoName);
                var newRepo = new GitHubRepository
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    Owner = repo.Owner,
                    Name = repo.RepoName,
                    CloneUrl = repo.CloneUrl,
                    LastAnalyzedCommitDate = null // Null until first analysis completes
                };

                _logger.LogInformation("Saving repository {Owner}/{Name} to database with ID {Id}",
                    newRepo.Owner, newRepo.Name, newRepo.Id);
                await _repositoryDataService.AddRepositoryAsync(newRepo);

                _logger.LogInformation("Successfully saved repository {Owner}/{Name}. Adding to result list.",
                    newRepo.Owner, newRepo.Name);
                addedRepositories.Add(newRepo);
                repositoriesToAnalyze.Add(newRepo.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION while adding repository {Owner}/{Name}: {Message}. Stack: {StackTrace}",
                    repo.Owner ?? "NULL", repo.RepoName ?? "NULL", ex.Message, ex.StackTrace);
                // Continue with other repositories even if one fails
            }
        }

        _logger.LogInformation("Phase 1 complete: Added {Count} repositories to database. Analysis will be queued by the caller.", addedRepositories.Count);

        _logger.LogInformation("=== COMPLETED AddMultipleRepositoriesCommandHandler ===");
        _logger.LogInformation("Final result: Successfully added {Count} out of {Total} repositories.",
            addedRepositories.Count, request.Repositories.Count());
        _logger.LogInformation("Repository IDs added: {Ids}",
            string.Join(", ", addedRepositories.Select(r => $"{r.Owner}/{r.Name} ({r.Id})")));

        return addedRepositories;
    }
}
