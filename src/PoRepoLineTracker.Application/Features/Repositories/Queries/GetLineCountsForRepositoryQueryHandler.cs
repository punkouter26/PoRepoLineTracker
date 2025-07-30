using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetLineCountsForRepositoryQueryHandler : IRequestHandler<GetLineCountsForRepositoryQuery, IEnumerable<CommitLineCount>>
    {
        private readonly IRepositoryDataService _repositoryDataService;

        public GetLineCountsForRepositoryQueryHandler(IRepositoryDataService repositoryDataService)
        {
            _repositoryDataService = repositoryDataService;
        }

        public async Task<IEnumerable<CommitLineCount>> Handle(GetLineCountsForRepositoryQuery request, CancellationToken cancellationToken)
        {
            return await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId);
        }
    }
}
