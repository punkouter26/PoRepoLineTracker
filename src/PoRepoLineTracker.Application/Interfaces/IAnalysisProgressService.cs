using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Shared.Models;

namespace PoRepoLineTracker.Application.Interfaces;

/// <summary>
/// Tracks live analysis progress for repository jobs running in background tasks.
/// Implemented as a singleton so background Task.Run jobs share state with the API request threads.
/// </summary>
public interface IAnalysisProgressService
{
    /// <summary>Report that a job has reached a new step.</summary>
    void ReportStep(RepositoryId repositoryId, int stepIndex, string stepName, string stepDescription);

    /// <summary>Report how many commits have been found (before processing starts).</summary>
    void ReportCommitsFound(RepositoryId repositoryId, int total);

    /// <summary>Report progress within the commit processing loop.</summary>
    void ReportCommitProgress(RepositoryId repositoryId, int processed, int total);

    /// <summary>Mark a job as finished (success).</summary>
    void ReportComplete(RepositoryId repositoryId);

    /// <summary>Mark a job as failed with an error message.</summary>
    void ReportError(RepositoryId repositoryId, string errorMessage);

    /// <summary>Get the current progress snapshot for a repository. Returns null if no job is tracked.</summary>
    AnalysisProgressDto? GetProgress(RepositoryId repositoryId);
}
