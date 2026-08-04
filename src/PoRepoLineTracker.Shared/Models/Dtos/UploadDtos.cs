using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Wire shape for a successful <c>POST /api/repositories/upload-zip</c>.
/// Previously an anonymous type on the API and a private mirror inside UploadRepository.razor;
/// both are now this single contract so the payload can be source-generated (Rule 1.2).
/// </summary>
public sealed class UploadResult
{
    public RepositoryId Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>Wire shape for a failed upload — a single human-readable reason.</summary>
public sealed class UploadError
{
    public string? Error { get; set; }
}

/// <summary>
/// Wire shape for <c>GET /api/antiforgery/token</c>.
///
/// The BFF client cannot read the antiforgery cookie (it is <c>HttpOnly</c>), so the request
/// token has to be handed to it explicitly; it then echoes it in the <c>X-CSRF-TOKEN</c> header
/// on every state-changing call. See <c>AntiforgeryMiddleware</c>.
/// </summary>
public sealed class AntiforgeryTokenResponse
{
    public string Token { get; set; } = string.Empty;

    /// <summary>Header the server expects the token echoed in.</summary>
    public string HeaderName { get; set; } = string.Empty;
}
