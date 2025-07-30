using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetConfiguredFileExtensionsQueryHandler : IRequestHandler<GetConfiguredFileExtensionsQuery, IEnumerable<string>>
    {
        private readonly IRepositoryDataService _repositoryDataService;

        public GetConfiguredFileExtensionsQueryHandler(IRepositoryDataService repositoryDataService)
        {
            _repositoryDataService = repositoryDataService;
        }

        public async Task<IEnumerable<string>> Handle(GetConfiguredFileExtensionsQuery request, CancellationToken cancellationToken)
        {
            return await _repositoryDataService.GetConfiguredFileExtensionsAsync();
        }
    }
}
