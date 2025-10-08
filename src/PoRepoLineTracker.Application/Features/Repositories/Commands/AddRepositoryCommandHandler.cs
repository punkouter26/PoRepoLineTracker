using MediatR;
using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
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
