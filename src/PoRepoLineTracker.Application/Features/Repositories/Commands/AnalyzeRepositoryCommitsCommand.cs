using MediatR;
using System;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands
{
    /// <summary>
    /// Command to analyze commits for a repository.
    /// </summary>
    /// <param name="RepositoryId">The repository to analyze</param>
    /// <param name="ForceReanalysis">If true, re-analyze commits that have missing diff data</param>
    /// <param name="ClearExistingData">If true, delete all existing commit data and re-analyze from scratch</param>
    public record AnalyzeRepositoryCommitsCommand(
        Guid RepositoryId, 
        bool ForceReanalysis = false,
        bool ClearExistingData = false) : IRequest<Unit>;
}
