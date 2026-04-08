using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public record GetLineCountHistoryQuery(Guid RepositoryId, int Days) : IRequest<IEnumerable<DailyLineCountDto>>;

public class GetLineCountHistoryQueryHandler : IRequestHandler<GetLineCountHistoryQuery, IEnumerable<DailyLineCountDto>>
{
    private readonly IRepositoryDataService _repositoryDataService;

    public GetLineCountHistoryQueryHandler(IRepositoryDataService repositoryDataService)
        => _repositoryDataService = repositoryDataService;

    public Task<IEnumerable<DailyLineCountDto>> Handle(GetLineCountHistoryQuery request, CancellationToken cancellationToken)
        => _repositoryDataService.GetLineCountHistoryAsync(request.RepositoryId, request.Days);
}
