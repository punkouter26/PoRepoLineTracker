using MediatR;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace PoRepoLineTracker.API.Features.Repositories;

public record DeleteRepositoryCommand(RepositoryId RepositoryId) : IRequest<Unit>;

public class DeleteRepositoryCommandHandler : IRequestHandler<DeleteRepositoryCommand, Unit>
{
    private readonly IRepositoryDataService _repositoryDataService;

    public DeleteRepositoryCommandHandler(IRepositoryDataService repositoryDataService)
    {
        _repositoryDataService = repositoryDataService;
    }

    public async Task<Unit> Handle(DeleteRepositoryCommand request, CancellationToken cancellationToken)
    {
        await _repositoryDataService.DeleteRepositoryAsync(request.RepositoryId);
        return Unit.Value;
    }
}
