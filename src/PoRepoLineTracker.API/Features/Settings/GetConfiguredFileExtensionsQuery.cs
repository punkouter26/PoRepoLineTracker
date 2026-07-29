using MediatR;

namespace PoRepoLineTracker.API.Features.Settings;

public record GetConfiguredFileExtensionsQuery() : IRequest<IEnumerable<string>>;

public class GetConfiguredFileExtensionsQueryHandler : IRequestHandler<GetConfiguredFileExtensionsQuery, IEnumerable<string>>
{
    private readonly IRepositoryDataService _repositoryDataService;

    public GetConfiguredFileExtensionsQueryHandler(IRepositoryDataService repositoryDataService)
        => _repositoryDataService = repositoryDataService;

    public Task<IEnumerable<string>> Handle(GetConfiguredFileExtensionsQuery request, CancellationToken cancellationToken)
        => _repositoryDataService.GetConfiguredFileExtensionsAsync();
}
