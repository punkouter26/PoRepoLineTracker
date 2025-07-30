using MediatR;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public record AddRepositoryCommand(string Owner, string RepoName, string CloneUrl) : IRequest<GitHubRepository>;
}
