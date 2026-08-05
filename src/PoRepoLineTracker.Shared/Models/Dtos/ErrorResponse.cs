namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Standard error payload returned by the API for 4xx/5xx responses.
/// Shared between the API (return type on error paths) and the Blazor WASM client (deserialization target).
/// </summary>
public sealed class ErrorResponse
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string error { get; set; } = string.Empty;  // lowercase to match JSON
    public int Status { get; set; }
    public string? Type { get; set; }
}
