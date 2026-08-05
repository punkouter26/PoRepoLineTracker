using MediatR;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using PoRepoLineTracker.API.Telemetry;
using System.Diagnostics;

namespace PoRepoLineTracker.API.Features.Repositories;

/// <summary>
/// The single write path for tracking repositories. A one-element list is the "add one" case —
/// there is no separate AddRepositoryCommand, and deliberately so: the one that used to exist
/// inserted without checking for an existing row, so adding the same repository twice through it
/// produced two records for one GitHub repo.
/// </summary>
public record AddMultipleRepositoriesCommand(IEnumerable<BulkRepositoryDto> Repositories, UserId UserId) : IRequest<BulkAddResult>;

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
        // Span and metrics carried over from the deleted single-add handler, which was the only
        // add path that reported any. Now that this is the only one, it is where they belong.
        using var activity = AppTelemetry.ActivitySource.StartActivity("AddRepositories");
        var stopwatch = Stopwatch.StartNew();

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
                    Id = RepositoryId.New(),
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

                AppTelemetry.RepositoriesAdded.Add(1,
                    new KeyValuePair<string, object?>("status", "success"),
                    new KeyValuePair<string, object?>("owner", repo.Owner));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION while adding repository {Owner}/{Name}: {Message}",
                    repo.Owner ?? "NULL", repo.RepoName ?? "NULL", ex.Message);

                AppTelemetry.RepositoriesAdded.Add(1,
                    new KeyValuePair<string, object?>("status", "failure"),
                    new KeyValuePair<string, object?>("owner", repo.Owner ?? "unknown"),
                    new KeyValuePair<string, object?>("error.type", ex.GetType().Name));

                // Continue with other repositories even if one fails
            }
        }

        stopwatch.Stop();
        AppTelemetry.AddRepositoryDuration.Record(stopwatch.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("count", repoList.Count));

        activity?.SetTag("repositories.requested", repoList.Count);
        activity?.SetTag("repositories.added", added.Count);
        activity?.SetTag("repositories.already_tracked", alreadyTracked.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation("=== COMPLETED AddMultipleRepositoriesCommandHandler === Added={Added}, AlreadyTracked={AlreadyTracked} in {ElapsedMs}ms",
            added.Count, alreadyTracked.Count, stopwatch.ElapsedMilliseconds);

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
