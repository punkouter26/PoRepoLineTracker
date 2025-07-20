namespace PoRepoLineTracker.Client.Models;

public class ErrorResponse
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string error { get; set; } = string.Empty;  // lowercase to match JSON
    public int Status { get; set; }
    public string? Type { get; set; }
}
