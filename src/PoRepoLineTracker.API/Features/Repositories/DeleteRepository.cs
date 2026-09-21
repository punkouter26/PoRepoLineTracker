using System.Threading.Tasks;
using System.Threading;
using System;

namespace PoRepoLineTracker.API.Features.Repositories;

public record DeleteRepositoryCommand(RepositoryId RepositoryId);

public class DeleteRepositoryCommandHandler
{
    private readonly IRepositoryDataService _repositoryDataService;

    public DeleteRepositoryCommandHandler(IRepositoryDataService repositoryDataService)
    {
        _repositoryDataService = repositoryDataService;
    }

    public Task Handle(DeleteRepositoryCommand request, CancellationToken cancellationToken = default)
        => _repositoryDataService.DeleteRepositoryAsync(request.RepositoryId);
}
