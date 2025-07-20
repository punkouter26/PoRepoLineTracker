namespace PoRepoLineTracker.Application.Models;

public class RepositoryLineCountHistoryDto
{
    public Guid RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public IEnumerable<DailyLineCountDto> DailyLineCounts { get; set; } = new List<DailyLineCountDto>();
}
