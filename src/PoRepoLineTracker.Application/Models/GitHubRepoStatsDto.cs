using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Models;

public class GitHubRepoStatsDto
{
    public GitHubRepository Repository { get; set; } = new();
    public int CommitCount { get; set; }
    public List<CommitLineCount> CommitLineCounts { get; set; } = new();
    public Dictionary<string, int> LineCounts { get; set; } = new();
}
