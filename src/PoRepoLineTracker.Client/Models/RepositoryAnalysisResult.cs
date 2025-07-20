using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Client.Models;

public class RepositoryAnalysisResult
{
    public Guid RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public List<CommitLineCount> Commits { get; set; } = new();
}
