using MediatR;
using System;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public record DeleteRepositoryCommand(Guid RepositoryId) : IRequest<Unit>;
}
