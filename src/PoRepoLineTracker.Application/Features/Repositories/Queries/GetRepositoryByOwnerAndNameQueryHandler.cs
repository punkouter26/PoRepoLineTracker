using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetRepositoryByOwnerAndNameQueryHandler : IRequestHandler<GetRepositoryByOwnerAndNameQuery, GitHubRepoStatsDto?>
    {
        private readonly IRepositoryService _repositoryService;

        public GetRepositoryByOwnerAndNameQueryHandler(IRepositoryService repositoryService)
        {
            _repositoryService = repositoryService;
        }

        public async Task<GitHubRepoStatsDto?> Handle(GetRepositoryByOwnerAndNameQuery request, CancellationToken cancellationToken)
        {
            return await _repositoryService.GetRepositoryByOwnerAndNameAsync(request.Owner, request.RepoName);
        }
    }
}
