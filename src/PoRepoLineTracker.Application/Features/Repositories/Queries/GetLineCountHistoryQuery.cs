using PoRepoLineTracker.Domain.Models;
using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public record GetLineCountHistoryQuery(RepositoryId RepositoryId, int Days) : IRequest<IEnumerable<DailyLineCountDto>>;

public class GetLineCountHistoryQueryHandler : IRequestHandler<GetLineCountHistoryQuery, IEnumerable<DailyLineCountDto>>
{
    private readonly IRepositoryDataService _repositoryDataService;

    public GetLineCountHistoryQueryHandler(IRepositoryDataService repositoryDataService)
        => _repositoryDataService = repositoryDataService;

    public Task<IEnumerable<DailyLineCountDto>> Handle(GetLineCountHistoryQuery request, CancellationToken cancellationToken)
        => _repositoryDataService.GetLineCountHistoryAsync(request.RepositoryId, request.Days);
}
