namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Wire shape for <c>GET /api/antiforgery/token</c>.
///
/// <para>The client cannot read the antiforgery cookie — it is <c>HttpOnly</c> — so the request
/// token has to be handed over explicitly; the client then echoes it in the <c>X-CSRF-TOKEN</c>
/// header on every state-changing call. Both halves are required: missing either gives 400. See
/// <c>AntiforgeryMiddleware</c>.</para>
///
/// <para>This lived in <c>UploadDtos.cs</c> until the ZIP-upload feature was removed, which took an
/// unrelated and still-load-bearing contract with it. It has its own file now so the next feature
/// deletion cannot repeat that.</para>
/// </summary>
public sealed class AntiforgeryTokenResponse
{
    public string Token { get; set; } = string.Empty;

    /// <summary>Header the server expects the token echoed in.</summary>
    public string HeaderName { get; set; } = string.Empty;
}
