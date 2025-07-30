using MediatR;
using PoRepoLineTracker.Domain.Models;
using System;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetLineCountsForRepositoryQuery(Guid RepositoryId) : IRequest<IEnumerable<CommitLineCount>>;
}
