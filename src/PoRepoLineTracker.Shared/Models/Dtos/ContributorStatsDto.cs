namespace PoRepoLineTracker.Shared.Models.Dtos;

public record ContributorStatsDto
{
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorEmail { get; init; } = string.Empty;
    public int TotalCommits { get; init; }
    public int TotalLinesAdded { get; init; }
    public int TotalLinesRemoved { get; init; }
    public int NetLines => TotalLinesAdded - TotalLinesRemoved;
    public double AverageLinesPerCommit => TotalCommits > 0 ? (double)TotalLinesAdded / TotalCommits : 0;
    public double PercentageOfTotalLines { get; init; }
    public List<DailyContributorStatsDto> DailyHistory { get; init; } = new();
}

public record DailyContributorStatsDto
{
    public DateTime Date { get; init; }
    public int LinesAdded { get; init; }
    public int Commits { get; init; }
}
