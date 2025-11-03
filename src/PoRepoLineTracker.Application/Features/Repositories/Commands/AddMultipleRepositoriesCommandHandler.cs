using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public class AddMultipleRepositoriesCommandHandler : IRequestHandler<AddMultipleRepositoriesCommand, IEnumerable<GitHubRepository>>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<AddMultipleRepositoriesCommandHandler> _logger;
    private readonly IMediator _mediator; // Inject IMediator

    public AddMultipleRepositoriesCommandHandler(
        IRepositoryDataService repositoryDataService,
        ILogger<AddMultipleRepositoriesCommandHandler> logger,
        IMediator mediator) // Add IMediator to constructor
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
        _mediator = mediator; // Assign mediator
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
        var repositoriesToAnalyze = new List<Guid>(); // Track repos that need analysis

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

                // Check if repository already exists
                _logger.LogInformation("Checking if repository {Owner}/{Name} already exists...", repo.Owner, repo.RepoName);
                var existingRepo = await _repositoryDataService.GetRepositoryByOwnerAndNameAsync(repo.Owner, repo.RepoName);
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
                repositoriesToAnalyze.Add(newRepo.Id); // Queue for analysis
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION while adding repository {Owner}/{Name}: {Message}. Stack: {StackTrace}",
                    repo.Owner ?? "NULL", repo.RepoName ?? "NULL", ex.Message, ex.StackTrace);
                // Continue with other repositories even if one fails
            }
        }

        _logger.LogInformation("Phase 1 complete: Added {Count} repositories to database", addedRepositories.Count);

        // PHASE 2: Dispatch analysis commands (fire-and-forget to avoid blocking return)
        if (repositoriesToAnalyze.Any())
        {
            _logger.LogInformation("Phase 2: Dispatching analysis for {Count} repositories", repositoriesToAnalyze.Count);

            // Run analysis in background without awaiting (fire-and-forget pattern)
            _ = Task.Run(async () =>
            {
                foreach (var repoId in repositoriesToAnalyze)
                {
                    try
                    {
                        var repo = addedRepositories.FirstOrDefault(r => r.Id == repoId);
                        _logger.LogInformation("Dispatching AnalyzeRepositoryCommitsCommand for repository ID: {Id} ({Owner}/{Name})",
                            repoId, repo?.Owner ?? "Unknown", repo?.Name ?? "Unknown");

                        await _mediator.Send(new AnalyzeRepositoryCommitsCommand(repoId), cancellationToken);

                        _logger.LogInformation("Analysis command completed for repository ID: {Id}", repoId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "EXCEPTION during analysis of repository ID: {Id}. Message: {Message}",
                            repoId, ex.Message);
                        // Continue with other analyses even if one fails
                    }
                }
            }, cancellationToken);

            _logger.LogInformation("Analysis dispatched in background for {Count} repositories", repositoriesToAnalyze.Count);
        }

        _logger.LogInformation("=== COMPLETED AddMultipleRepositoriesCommandHandler ===");
        _logger.LogInformation("Final result: Successfully added {Count} out of {Total} repositories.",
            addedRepositories.Count, request.Repositories.Count());
        _logger.LogInformation("Repository IDs added: {Ids}",
            string.Join(", ", addedRepositories.Select(r => $"{r.Owner}/{r.Name} ({r.Id})")));

        return addedRepositories;
    }
}
