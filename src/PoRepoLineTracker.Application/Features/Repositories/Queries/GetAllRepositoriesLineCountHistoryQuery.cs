using MediatR;
using PoRepoLineTracker.Application.Models;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetAllRepositoriesLineCountHistoryQuery(int Days) : IRequest<IEnumerable<RepositoryLineCountHistoryDto>>;
}
