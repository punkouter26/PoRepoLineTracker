using MediatR;
using PoRepoLineTracker.Application.Models;
using System;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetLineCountHistoryQuery(Guid RepositoryId, int Days) : IRequest<IEnumerable<DailyLineCountDto>>;
}
