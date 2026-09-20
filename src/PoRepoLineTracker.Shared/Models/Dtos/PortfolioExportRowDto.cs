namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Flat summary record of a repository for portfolio CSV and JSON exports.
/// </summary>
public sealed class PortfolioExportRowDto
{
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? TotalLines { get; set; }
    public DateTime? LastAnalyzedUtc { get; set; }
    public string CloneUrl { get; set; } = string.Empty;
}

