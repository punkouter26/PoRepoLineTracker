using MediatR;
using System;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    public record AnalyzeRepositoryCommitsCommand(Guid RepositoryId, bool ForceReanalysis = false) : IRequest<Unit>;
}
