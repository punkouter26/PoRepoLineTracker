namespace PoRepoLineTracker.Shared.Models.Dtos;

public class FileExtensionPercentageDto
{
    public string FileExtension { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public int LineCount { get; set; }
}
