using PoRepoLineTracker.Domain.Models;
namespace PoRepoLineTracker.Shared.Models;

/// <summary>
/// Represents live progress for a repository analysis job.
/// Returned by GET /api/repositories/{id}/analysis-progress.
/// </summary>
public sealed class AnalysisProgressDto
{
    public RepositoryId RepositoryId { get; set; }

    /// <summary>Human-readable description of the current step, e.g. "Cloning repository (step 1/4)".</summary>
    public string StepDescription { get; set; } = string.Empty;

    /// <summary>Short step name for badge display, e.g. "Cloning", "Fetching", "Processing", "Saving".</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>1-based index of the current step.</summary>
    public int StepIndex { get; set; }

    /// <summary>Total number of steps.</summary>
    public int StepTotal { get; set; } = 4;

    /// <summary>Number of commits processed so far (0 when not yet in the commit loop).</summary>
    public int CommitsProcessed { get; set; }

    /// <summary>Total commits to process (0 until fetched).</summary>
    public int CommitsTotal { get; set; }

    /// <summary>UTC timestamp of the last progress update.</summary>
    public DateTime LastUpdatedUtc { get; set; }

    /// <summary>True when the analysis job is actively running.</summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// True when the job appears stuck — no progress update received in the last 3 minutes
    /// while still marked as running.
    /// </summary>
    public bool IsStuck => IsRunning && (DateTime.UtcNow - LastUpdatedUtc) > TimeSpan.FromMinutes(3);

    /// <summary>Error message if the job failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Percentage complete (0–100), based on commits when available, or step index otherwise.</summary>
    public int ProgressPercent =>
        CommitsTotal > 0
            ? (int)Math.Round((double)CommitsProcessed / CommitsTotal * 100)
            : StepTotal > 0
                ? (int)Math.Round((double)(StepIndex - 1) / StepTotal * 100)
                : 0;
}
