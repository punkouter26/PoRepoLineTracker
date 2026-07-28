using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public record GetAllRepositoriesQuery(UserId UserId) : IRequest<IEnumerable<GitHubRepository>>;

public class GetAllRepositoriesQueryHandler : IRequestHandler<GetAllRepositoriesQuery, IEnumerable<GitHubRepository>>
{
    private readonly IRepositoryDataService _repositoryDataService;

    public GetAllRepositoriesQueryHandler(IRepositoryDataService repositoryDataService)
        => _repositoryDataService = repositoryDataService;

    public Task<IEnumerable<GitHubRepository>> Handle(GetAllRepositoriesQuery request, CancellationToken cancellationToken)
        => _repositoryDataService.GetAllRepositoriesAsync(request.UserId);
}
