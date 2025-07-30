using MediatR;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public record AddMultipleRepositoriesCommand(IEnumerable<BulkRepositoryDto> Repositories) : IRequest<IEnumerable<GitHubRepository>>;
