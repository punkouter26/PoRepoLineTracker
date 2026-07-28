using PoRepoLineTracker.Domain.Models;
using MediatR;
using PoRepoLineTracker.Shared.Models.Dtos;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetAllRepositoriesLineCountHistoryQuery(int Days, UserId UserId) : IRequest<IEnumerable<RepositoryLineCountHistoryDto>>;
}
