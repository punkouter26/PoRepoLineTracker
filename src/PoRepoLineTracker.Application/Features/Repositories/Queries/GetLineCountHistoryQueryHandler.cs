using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetLineCountHistoryQueryHandler : IRequestHandler<GetLineCountHistoryQuery, IEnumerable<DailyLineCountDto>>
    {
        private readonly IRepositoryService _repositoryService;

        public GetLineCountHistoryQueryHandler(IRepositoryService repositoryService)
        {
            _repositoryService = repositoryService;
        }

        public async Task<IEnumerable<DailyLineCountDto>> Handle(GetLineCountHistoryQuery request, CancellationToken cancellationToken)
        {
            return await _repositoryService.GetLineCountHistoryAsync(request.RepositoryId, request.Days);
        }
    }
}
