using PoRepoLineTracker.Domain.Models;
namespace PoRepoLineTracker.Shared.Models.Dtos;

public class RepositoryLineCountHistoryDto
{
    public RepositoryId RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public IEnumerable<DailyLineCountDto> DailyLineCounts { get; set; } = new List<DailyLineCountDto>();
}
