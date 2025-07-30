using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public class DeleteRepositoryCommandHandler : IRequestHandler<DeleteRepositoryCommand, Unit>
    {
        private readonly IRepositoryService _repositoryService;

        public DeleteRepositoryCommandHandler(IRepositoryService repositoryService)
        {
            _repositoryService = repositoryService;
        }

        public async Task<Unit> Handle(DeleteRepositoryCommand request, CancellationToken cancellationToken)
        {
            await _repositoryService.DeleteRepositoryAsync(request.RepositoryId);
            return Unit.Value;
        }
    }
}
