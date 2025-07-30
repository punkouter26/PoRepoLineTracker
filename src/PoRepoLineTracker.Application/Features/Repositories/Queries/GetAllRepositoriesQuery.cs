using MediatR;
using PoRepoLineTracker.Domain.Models;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetAllRepositoriesQuery() : IRequest<IEnumerable<GitHubRepository>>;
}
