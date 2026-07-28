using PoRepoLineTracker.Domain.Models;
using System.Collections.Concurrent;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models;

namespace PoRepoLineTracker.Application.Services;

/// <summary>
/// In-memory singleton that stores live analysis progress for background jobs.
/// Safe for concurrent access from multiple background Task.Run threads.
/// </summary>
public sealed class AnalysisProgressService : IAnalysisProgressService
{
    private readonly ConcurrentDictionary<RepositoryId, AnalysisProgressDto> _progress = new();

    public void ReportStep(RepositoryId repositoryId, int stepIndex, string stepName, string stepDescription)
    {
        var dto = _progress.GetOrAdd(repositoryId, _ => new AnalysisProgressDto { RepositoryId = repositoryId });
        dto.StepIndex = stepIndex;
        dto.StepName = stepName;
        dto.StepDescription = stepDescription;
        dto.IsRunning = true;
        dto.ErrorMessage = null;
        dto.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void ReportCommitsFound(RepositoryId repositoryId, int total)
    {
        if (_progress.TryGetValue(repositoryId, out var dto))
        {
            dto.CommitsTotal = total;
            dto.CommitsProcessed = 0;
            dto.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public void ReportCommitProgress(RepositoryId repositoryId, int processed, int total)
    {
        if (_progress.TryGetValue(repositoryId, out var dto))
        {
            dto.CommitsProcessed = processed;
            dto.CommitsTotal = total;
            dto.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public void ReportComplete(RepositoryId repositoryId)
    {
        if (_progress.TryGetValue(repositoryId, out var dto))
        {
            dto.IsRunning = false;
            dto.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public void ReportError(RepositoryId repositoryId, string errorMessage)
    {
        var dto = _progress.GetOrAdd(repositoryId, _ => new AnalysisProgressDto { RepositoryId = repositoryId });
        dto.IsRunning = false;
        dto.ErrorMessage = errorMessage;
        dto.LastUpdatedUtc = DateTime.UtcNow;
    }

    public AnalysisProgressDto? GetProgress(RepositoryId repositoryId) =>
        _progress.TryGetValue(repositoryId, out var dto) ? dto : null;
}
