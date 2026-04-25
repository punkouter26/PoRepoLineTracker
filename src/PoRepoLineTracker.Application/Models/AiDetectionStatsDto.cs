namespace PoRepoLineTracker.Application.Models;

/// <summary>
/// Represents AI detection statistics for a specific user across all their commits.
/// </summary>
public class AiDetectionStatsDto
{
    /// <summary>
    /// The username of the author.
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// The author's email address.
    /// </summary>
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>
    /// Total number of commits analyzed.
    /// </summary>
    public int TotalCommits { get; set; }

    /// <summary>
    /// Average AI detection percentage across all commits (0-100).
    /// </summary>
    public double AverageAiPercentage { get; set; }

    /// <summary>
    /// Total lines of code analyzed.
    /// </summary>
    public int TotalLinesAnalyzed { get; set; }

    /// <summary>
    /// Breakdown by commit showing AI percentage per commit.
    /// </summary>
    public List<AiDetectionPerCommitDto> Commits { get; set; } = new();
}

/// <summary>
/// Represents AI detection result for a single commit.
/// </summary>
public class AiDetectionPerCommitDto
{
    /// <summary>
    /// The commit SHA.
    /// </summary>
    public string CommitSha { get; set; } = string.Empty;

    /// <summary>
    /// The commit date.
    /// </summary>
    public DateTime CommitDate { get; set; }

    /// <summary>
    /// AI detection percentage (0-100).
    /// </summary>
    public double AiPercentage { get; set; }

    /// <summary>
    /// Number of lines in the commit.
    /// </summary>
    public int LinesAdded { get; set; }

    /// <summary>
    /// Lines removed in the commit.
    /// </summary>
    public int LinesRemoved { get; set; }
}

/// <summary>
/// Represents daily AI detection statistics for charting.
/// </summary>
public class DailyAiDetectionDto
{
    /// <summary>
    /// The date.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Average AI percentage for that day.
    /// </summary>
    public double AverageAiPercentage { get; set; }

    /// <summary>
    /// Number of commits on that day.
    /// </summary>
    public int CommitCount { get; set; }

    /// <summary>
    /// Breakdown by author.
    /// </summary>
    public Dictionary<string, double> AuthorBreakdown { get; set; } = new();
}

/// <summary>
/// Represents AI detection data for a specific user per commit for graphing.
/// </summary>
public class UserAiPercentagePerCommitDto
{
    /// <summary>
    /// The commit date.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// The commit SHA.
    /// </summary>
    public string CommitSha { get; set; } = string.Empty;

    /// <summary>
    /// AI percentage for this commit.
    /// </summary>
    public double AiPercentage { get; set; }

    /// <summary>
    /// Lines added in this commit.
    /// </summary>
    public int LinesAdded { get; set; }
}

/// <summary>
/// Represents AI statistics grouped by user for a repository.
/// </summary>
public class AiStatsByUserDto
{
    /// <summary>
    /// The username.
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Average AI percentage for this user.
    /// </summary>
    public double AverageAiPercentage { get; set; }

    /// <summary>
    /// Total commits by this user.
    /// </summary>
    public int TotalCommits { get; set; }

    /// <summary>
    /// Data points for the graph.
    /// </summary>
    public List<UserAiPercentagePerCommitDto> CommitHistory { get; set; } = new();
}
