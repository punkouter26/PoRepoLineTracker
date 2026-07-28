using PoRepoLineTracker.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public record GetTopFilesQuery(RepositoryId RepositoryId, int Count = 5) : IRequest<IEnumerable<TopFileDto>>;

public class GetTopFilesQueryHandler : IRequestHandler<GetTopFilesQuery, IEnumerable<TopFileDto>>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<GetTopFilesQueryHandler> _logger;

    public GetTopFilesQueryHandler(IRepositoryDataService repositoryDataService, ILogger<GetTopFilesQueryHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<TopFileDto>> Handle(GetTopFilesQuery request, CancellationToken cancellationToken)
    {
        var topFiles = await _repositoryDataService.GetTopFilesAsync(request.RepositoryId, request.Count);
        if (!topFiles.Any())
            _logger.LogWarning("No top files found in storage for repository {RepositoryId}.", request.RepositoryId);
        return topFiles;
    }
}
