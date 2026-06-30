using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Domain.Models;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public class AddMultipleRepositoriesCommandHandler : IRequestHandler<AddMultipleRepositoriesCommand, BulkAddResult>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<AddMultipleRepositoriesCommandHandler> _logger;

    public AddMultipleRepositoriesCommandHandler(
        IRepositoryDataService repositoryDataService,
        ILogger<AddMultipleRepositoriesCommandHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<BulkAddResult> Handle(AddMultipleRepositoriesCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== START AddMultipleRepositoriesCommandHandler ===");
        _logger.LogInformation("Received request to add {Count} repositories", request.Repositories.Count());

        var repoList = request.Repositories.ToList();
        for (int i = 0; i < repoList.Count; i++)
        {
            var repo = repoList[i];
            _logger.LogInformation("Repository [{Index}]: Owner='{Owner}', Name='{RepoName}', CloneUrl='{CloneUrl}'",
                i, repo.Owner ?? "NULL", repo.RepoName ?? "NULL", repo.CloneUrl ?? "NULL");
        }

        var added = new List<GitHubRepository>();
        var alreadyTracked = new List<GitHubRepository>();

        // PHASE 1: Add repositories to database
        foreach (var repo in request.Repositories)
        {
            try
            {
                _logger.LogInformation("Processing repository: {Owner}/{Name}", repo.Owner, repo.RepoName);

                if (string.IsNullOrWhiteSpace(repo.Owner))
                {
                    _logger.LogWarning("Skipping repository with empty Owner. RepoName={RepoName}", repo.RepoName ?? "NULL");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(repo.RepoName))
                {
                    _logger.LogWarning("Skipping repository with empty RepoName. Owner={Owner}", repo.Owner ?? "NULL");
                    continue;
                }

                _logger.LogInformation("Checking if repository {Owner}/{Name} already exists for user {UserId}...", repo.Owner, repo.RepoName, request.UserId);
                var existingRepo = await _repositoryDataService.GetRepositoryByOwnerAndNameAsync(repo.Owner, repo.RepoName, request.UserId);
                if (existingRepo != null)
                {
                    _logger.LogInformation("Repository {Owner}/{Name} already tracked with ID {Id} — returning in AlreadyTracked bucket.",
                        repo.Owner, repo.RepoName, existingRepo.Id);
                    alreadyTracked.Add(existingRepo);
                    continue;
                }

                _logger.LogInformation("Creating new repository entity for {Owner}/{Name}", repo.Owner, repo.RepoName);
                var newRepo = new GitHubRepository
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    Owner = repo.Owner,
                    Name = repo.RepoName,
                    CloneUrl = repo.CloneUrl,
                    LastAnalyzedCommitDate = null // null until first analysis completes
                };

                _logger.LogInformation("Saving repository {Owner}/{Name} to database with ID {Id}", newRepo.Owner, newRepo.Name, newRepo.Id);
                await _repositoryDataService.AddRepositoryAsync(newRepo);

                _logger.LogInformation("Successfully saved repository {Owner}/{Name}.", newRepo.Owner, newRepo.Name);
                added.Add(newRepo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION while adding repository {Owner}/{Name}: {Message}",
                    repo.Owner ?? "NULL", repo.RepoName ?? "NULL", ex.Message);
                // Continue with other repositories even if one fails
            }
        }

        _logger.LogInformation("=== COMPLETED AddMultipleRepositoriesCommandHandler === Added={Added}, AlreadyTracked={AlreadyTracked}",
            added.Count, alreadyTracked.Count);

        return new BulkAddResult
        {
            Added = [.. added.Select(ToDto)],
            AlreadyTracked = [.. alreadyTracked.Select(ToDto)]
        };
    }

    private static GitHubRepositoryDto ToDto(GitHubRepository repo) => new()
    {
        Id = repo.Id,
        UserId = repo.UserId,
        Owner = repo.Owner,
        Name = repo.Name,
        CloneUrl = repo.CloneUrl,
        LastAnalyzedCommitDate = repo.LastAnalyzedCommitDate,
        LocalPath = repo.LocalPath
    };
}
