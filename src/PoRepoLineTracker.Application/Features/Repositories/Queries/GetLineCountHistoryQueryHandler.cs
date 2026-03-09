using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetLineCountHistoryQueryHandler : IRequestHandler<GetLineCountHistoryQuery, IEnumerable<DailyLineCountDto>>
    {
        private readonly IRepositoryDataService _repositoryDataService;

        public GetLineCountHistoryQueryHandler(IRepositoryDataService repositoryDataService)
        {
            _repositoryDataService = repositoryDataService;
        }

        public async Task<IEnumerable<DailyLineCountDto>> Handle(GetLineCountHistoryQuery request, CancellationToken cancellationToken)
        {
            return await _repositoryDataService.GetLineCountHistoryAsync(request.RepositoryId, request.Days);
        }
    }
}
