using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Client.Models;

public class RepositoryAnalysisResult
{
    public Guid RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public IEnumerable<DailyLineCountDto> DailyLineCounts { get; set; } = new List<DailyLineCountDto>();
    public IEnumerable<FileExtensionPercentageDto> FileExtensionPercentages { get; set; } = new List<FileExtensionPercentageDto>();
}
