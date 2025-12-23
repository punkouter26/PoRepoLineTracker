using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Models;
using System.IO;
using MediatR; // Add MediatR using statement
using PoRepoLineTracker.Application.Features.Repositories.Commands; // Add Commands using statement
using PoRepoLineTracker.Application.Features.Repositories.Queries; // Add Queries using statement

namespace PoRepoLineTracker.Application.Services;

public class RepositoryService : IRepositoryService
{
    private readonly IMediator _mediator; // Use IMediator
    private readonly IRepositoryDataService _repositoryDataService; // Add data service for direct operations
    private readonly ILogger<RepositoryService> _logger; // Keep logger for service-level logging

    // Remove _fileExtensionsToCount as it will be handled by a query

    public RepositoryService(IMediator mediator, IRepositoryDataService repositoryDataService, ILogger<RepositoryService> logger)
    {
        _mediator = mediator;
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    [Obsolete("Use API endpoint with authentication instead. This method requires user context.")]
    public Task<IEnumerable<GitHubRepository>> GetAllRepositoriesAsync()
    {
        throw new NotSupportedException("GetAllRepositoriesAsync requires user context. Call the API endpoint directly with authentication.");
    }

    public async Task<GitHubRepoStatsDto?> GetRepositoryByOwnerAndNameAsync(string owner, string repoName)
    {
        _logger.LogInformation("Getting repository by Owner: {Owner} and Name: {RepoName} via MediatR.", owner, repoName);
        return await _mediator.Send(new GetRepositoryByOwnerAndNameQuery(owner, repoName));
    }

    public async Task<GitHubRepository> AddRepositoryAsync(string owner, string repoName, string cloneUrl)
    {
        _logger.LogInformation("Adding new repository: {Owner}/{RepoName} via MediatR.", owner, repoName);
        return await _mediator.Send(new AddRepositoryCommand(owner, repoName, cloneUrl));
    }

    [Obsolete("Use API endpoint with authentication instead. This method requires user context.")]
    public Task<IEnumerable<GitHubRepository>> AddMultipleRepositoriesAsync(IEnumerable<Application.Models.BulkRepositoryDto> repositories)
    {
        throw new NotSupportedException("AddMultipleRepositoriesAsync requires user context. Call the API endpoint directly with authentication.");
    }

    public async Task AnalyzeRepositoryCommitsAsync(Guid repositoryId)
    {
        _logger.LogInformation("Starting analysis for repository ID: {RepositoryId} via MediatR.", repositoryId);
        await _mediator.Send(new AnalyzeRepositoryCommitsCommand(repositoryId));
    }

    public async Task UpdateRepositoryAsync(GitHubRepository repository)
    {
        _logger.LogInformation("Updating repository: {Owner}/{Name} via MediatR.", repository.Owner, repository.Name);
        await _mediator.Send(new UpdateRepositoryCommand(repository));
    }

    public async Task<IEnumerable<CommitLineCount>> GetLineCountsForRepositoryAsync(Guid repositoryId)
    {
        _logger.LogInformation("Retrieving line counts for repository ID: {RepositoryId} via MediatR.", repositoryId);
        return await _mediator.Send(new GetLineCountsForRepositoryQuery(repositoryId));
    }

    public async Task<IEnumerable<DailyLineCountDto>> GetLineCountHistoryAsync(Guid repositoryId, int days)
    {
        _logger.LogInformation("Retrieving line count history for repository ID: {RepositoryId} for the last {Days} days via data service.", repositoryId, days);
        return await _repositoryDataService.GetLineCountHistoryAsync(repositoryId, days);
    }

    [Obsolete("Use API endpoint with authentication instead. This method requires user context.")]
    public Task<IEnumerable<RepositoryLineCountHistoryDto>> GetAllRepositoriesLineCountHistoryAsync(int days)
    {
        throw new NotSupportedException("GetAllRepositoriesLineCountHistoryAsync requires user context. Call the API endpoint directly with authentication.");
    }

    public async Task<IEnumerable<string>> GetConfiguredFileExtensionsAsync()
    {
        _logger.LogInformation("Retrieving configured file extensions via MediatR.");
        return await _mediator.Send(new GetConfiguredFileExtensionsQuery());
    }

    public async Task DeleteRepositoryAsync(Guid repositoryId)
    {
        _logger.LogInformation("Starting deletion process for repository {RepositoryId} via data service.", repositoryId);
        await _repositoryDataService.DeleteRepositoryAsync(repositoryId);
        _logger.LogInformation("Repository {RepositoryId} deleted successfully.", repositoryId);
    }

    [Obsolete("Use API endpoint with authentication instead. This method requires user context.")]
    public Task RemoveAllRepositoriesAsync()
    {
        throw new NotSupportedException("RemoveAllRepositoriesAsync requires user context. Call the API endpoint directly with authentication.");
    }

    public async Task<IEnumerable<FileExtensionPercentageDto>> GetFileExtensionPercentagesAsync(Guid repositoryId)
    {
        _logger.LogInformation("Calculating file extension percentages for repository ID: {RepositoryId} via MediatR.", repositoryId);
        return await _mediator.Send(new GetFileExtensionPercentagesQuery(repositoryId));
    }
}
