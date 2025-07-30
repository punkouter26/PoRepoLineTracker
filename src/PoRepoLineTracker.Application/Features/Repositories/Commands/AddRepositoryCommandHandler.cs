using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public class AddRepositoryCommandHandler : IRequestHandler<AddRepositoryCommand, GitHubRepository>
    {
        private readonly IRepositoryDataService _repositoryDataService;

        public AddRepositoryCommandHandler(IRepositoryDataService repositoryDataService)
        {
            _repositoryDataService = repositoryDataService;
        }

        public async Task<GitHubRepository> Handle(AddRepositoryCommand request, CancellationToken cancellationToken)
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
            return repository;
        }
    }
}
