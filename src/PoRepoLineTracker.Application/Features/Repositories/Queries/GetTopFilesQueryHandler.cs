using MediatR;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public class GetTopFilesQueryHandler : IRequestHandler<GetTopFilesQuery, IEnumerable<TopFileDto>>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly IGitHubService _gitHubService;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly ILogger<GetTopFilesQueryHandler> _logger;

    public GetTopFilesQueryHandler(
        IRepositoryDataService repositoryDataService,
        IGitHubService gitHubService,
        IUserPreferencesService userPreferencesService,
        ILogger<GetTopFilesQueryHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _gitHubService = gitHubService;
        _userPreferencesService = userPreferencesService;
        _logger = logger;
    }

    public async Task<IEnumerable<TopFileDto>> Handle(GetTopFilesQuery request, CancellationToken cancellationToken)
    {
        var repository = await _repositoryDataService.GetRepositoryByIdAsync(request.RepositoryId);
        if (repository == null || string.IsNullOrEmpty(repository.LocalPath))
        {
            _logger.LogWarning("Repository {RepositoryId} not found or has no local path", request.RepositoryId);
            return Enumerable.Empty<TopFileDto>();
        }

        var fileExtensions = repository.UserId != Guid.Empty
            ? await _userPreferencesService.GetFileExtensionsAsync(repository.UserId)
            : UserPreferences.DefaultFileExtensions;

        return await _gitHubService.GetTopFilesByLineCountAsync(repository.LocalPath, fileExtensions, request.Count);
    }
}
