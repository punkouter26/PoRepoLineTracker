using MediatR;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Telemetry;
using PoRepoLineTracker.Domain.Models;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public class AddRepositoryCommandHandler : IRequestHandler<AddRepositoryCommand, GitHubRepository>
    {
        private readonly IRepositoryDataService _repositoryDataService;
        private readonly ILogger<AddRepositoryCommandHandler> _logger;

        public AddRepositoryCommandHandler(
            IRepositoryDataService repositoryDataService,
            ILogger<AddRepositoryCommandHandler> logger)
        {
            _repositoryDataService = repositoryDataService;
            _logger = logger;
        }

        public async Task<GitHubRepository> Handle(AddRepositoryCommand request, CancellationToken cancellationToken)
        {
            // Start distributed trace
            using var activity = AppTelemetry.ActivitySource.StartActivity("AddRepository");
            activity?.SetTag("repository.owner", request.Owner);
            activity?.SetTag("repository.name", request.RepoName);
            activity?.SetTag("repository.clone_url", request.CloneUrl);

            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Adding repository: {Owner}/{RepoName}",
                request.Owner,
                request.RepoName);

            try
            {
                var repository = new GitHubRepository
                {
                    Id = Guid.NewGuid(),
                    Owner = request.Owner,
                    Name = request.RepoName,
                    CloneUrl = request.CloneUrl,
                    LastAnalyzedCommitDate = DateTime.MinValue // Default value indicating not analyzed yet
                };

                await _repositoryDataService.AddRepositoryAsync(repository);

                stopwatch.Stop();

                // Record successful metrics
                AppTelemetry.RepositoriesAdded.Add(1,
                    new KeyValuePair<string, object?>("status", "success"),
                    new KeyValuePair<string, object?>("owner", request.Owner));

                AppTelemetry.AddRepositoryDuration.Record(stopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("owner", request.Owner),
                    new KeyValuePair<string, object?>("name", request.RepoName));

                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("repository.id", repository.Id.ToString());

                _logger.LogInformation(
                    "Successfully added repository {Owner}/{RepoName} with ID {RepositoryId} in {ElapsedMs}ms",
                    request.Owner,
                    request.RepoName,
                    repository.Id,
                    stopwatch.ElapsedMilliseconds);

                return repository;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Record failure metrics
                AppTelemetry.RepositoriesAdded.Add(1,
                    new KeyValuePair<string, object?>("status", "failure"),
                    new KeyValuePair<string, object?>("owner", request.Owner),
                    new KeyValuePair<string, object?>("error.type", ex.GetType().Name));

                AppTelemetry.AddRepositoryDuration.Record(stopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("owner", request.Owner),
                    new KeyValuePair<string, object?>("name", request.RepoName));

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);

                _logger.LogError(
                    ex,
                    "Failed to add repository {Owner}/{RepoName} after {ElapsedMs}ms",
                    request.Owner,
                    request.RepoName,
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }
    }
}
