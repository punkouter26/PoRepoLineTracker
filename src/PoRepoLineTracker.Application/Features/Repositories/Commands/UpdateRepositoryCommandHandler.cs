using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public class UpdateRepositoryCommandHandler : IRequestHandler<UpdateRepositoryCommand, Unit>
    {
        private readonly IRepositoryService _repositoryService;

        public UpdateRepositoryCommandHandler(IRepositoryService repositoryService)
        {
            _repositoryService = repositoryService;
        }

        public async Task<Unit> Handle(UpdateRepositoryCommand request, CancellationToken cancellationToken)
        {
            await _repositoryService.UpdateRepositoryAsync(request.Repository);
            return Unit.Value;
        }
    }
}
