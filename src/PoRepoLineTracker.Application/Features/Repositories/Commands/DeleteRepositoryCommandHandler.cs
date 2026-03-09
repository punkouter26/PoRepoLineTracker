using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
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
}
