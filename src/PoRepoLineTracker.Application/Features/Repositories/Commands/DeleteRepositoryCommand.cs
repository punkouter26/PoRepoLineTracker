using PoRepoLineTracker.Domain.Models;
using MediatR;
using System;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public record DeleteRepositoryCommand(RepositoryId RepositoryId) : IRequest<Unit>;
}
