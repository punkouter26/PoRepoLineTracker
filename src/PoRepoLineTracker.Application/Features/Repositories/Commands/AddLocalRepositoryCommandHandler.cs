using MediatR;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public class AddLocalRepositoryCommandHandler : IRequestHandler<AddLocalRepositoryCommand, GitHubRepository>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<AddLocalRepositoryCommandHandler> _logger;

    public AddLocalRepositoryCommandHandler(
        IRepositoryDataService repositoryDataService,
        ILogger<AddLocalRepositoryCommandHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<GitHubRepository> Handle(AddLocalRepositoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Adding local repository: {Owner}/{Name} from path {LocalPath}",
            request.Owner,
            request.Name,
            request.LocalGitPath);

        var repository = new GitHubRepository
        {
            Id = RepositoryId.New(),
            UserId = request.UserId,
            Owner = request.Owner,
            Name = request.Name,
            CloneUrl = string.Empty, // No clone URL for local uploads
            LocalPath = request.LocalGitPath,
            LastAnalyzedCommitDate = null
        };

        await _repositoryDataService.AddRepositoryAsync(repository);

        _logger.LogInformation(
            "Successfully added local repository {Owner}/{Name} with ID {RepositoryId}",
            request.Owner,
            request.Name,
            repository.Id);

        return repository;
    }
}