using MediatR;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public record UpdateRepositoryCommand(GitHubRepository Repository) : IRequest<Unit>;
}
