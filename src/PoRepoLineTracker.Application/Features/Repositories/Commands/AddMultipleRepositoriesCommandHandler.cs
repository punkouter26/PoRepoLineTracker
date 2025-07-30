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
        _logger.LogInformation("Adding {Count} repositories to the system.", request.Repositories.Count());

        var addedRepositories = new List<GitHubRepository>();

        foreach (var repo in request.Repositories)
        {
            try
            {
                // Check if repository already exists
                var existingRepo = await _repositoryDataService.GetRepositoryByOwnerAndNameAsync(repo.Owner, repo.RepoName);
                if (existingRepo != null)
                {
                    _logger.LogInformation("Repository {Owner}/{Name} already exists, skipping.", repo.Owner, repo.RepoName);
                    addedRepositories.Add(existingRepo);
                    continue;
                }

                // Create new repository
                var newRepo = new GitHubRepository
                {
                    Id = Guid.NewGuid(),
                    Owner = repo.Owner,
                    Name = repo.RepoName,
                    CloneUrl = repo.CloneUrl,
                    LastAnalyzedCommitDate = DateTime.MinValue // Default value
                };

                await _repositoryDataService.AddRepositoryAsync(newRepo);
                addedRepositories.Add(newRepo);

                _logger.LogInformation("Successfully added repository {Owner}/{Name} with ID {Id}. Dispatching AnalyzeRepositoryCommitsCommand.",
                    newRepo.Owner, newRepo.Name, newRepo.Id);

                // Dispatch command to analyze the newly added repository
                await _mediator.Send(new AnalyzeRepositoryCommitsCommand(newRepo.Id), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add repository {Owner}/{Name}: {Message}",
                    repo.Owner, repo.RepoName, ex.Message);
                // Continue with other repositories even if one fails
            }
        }

        _logger.LogInformation("Successfully added {Count} out of {Total} repositories.",
            addedRepositories.Count, request.Repositories.Count());

        return addedRepositories;
    }
}
