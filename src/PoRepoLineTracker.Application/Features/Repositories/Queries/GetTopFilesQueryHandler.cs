using MediatR;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

/// <summary>
/// Handles the GetTopFilesQuery by retrieving pre-calculated top files from Azure Table Storage.
/// Top files are calculated and stored during repository analysis, so they're available
/// even without a local git clone (which is required in Azure Container Apps).
/// </summary>
public class GetTopFilesQueryHandler : IRequestHandler<GetTopFilesQuery, IEnumerable<TopFileDto>>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<GetTopFilesQueryHandler> _logger;

    public GetTopFilesQueryHandler(
        IRepositoryDataService repositoryDataService,
        ILogger<GetTopFilesQueryHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<TopFileDto>> Handle(GetTopFilesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting top {Count} files for repository {RepositoryId} from storage", 
            request.Count, request.RepositoryId);
        
        var topFiles = await _repositoryDataService.GetTopFilesAsync(request.RepositoryId, request.Count);
        
        if (!topFiles.Any())
        {
            _logger.LogWarning("No top files found in storage for repository {RepositoryId}. " +
                "This may indicate the repository hasn't been analyzed yet or analysis failed.", 
                request.RepositoryId);
        }
        
        return topFiles;
    }
}
