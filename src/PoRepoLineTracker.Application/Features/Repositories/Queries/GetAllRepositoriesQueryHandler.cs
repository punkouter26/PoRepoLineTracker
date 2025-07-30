using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetAllRepositoriesQueryHandler : IRequestHandler<GetAllRepositoriesQuery, IEnumerable<GitHubRepository>>
    {
        private readonly IRepositoryDataService _repositoryDataService;

        public GetAllRepositoriesQueryHandler(IRepositoryDataService repositoryDataService)
        {
            _repositoryDataService = repositoryDataService;
        }

        public async Task<IEnumerable<GitHubRepository>> Handle(GetAllRepositoriesQuery request, CancellationToken cancellationToken)
        {
            return await _repositoryDataService.GetAllRepositoriesAsync();
        }
    }
}
