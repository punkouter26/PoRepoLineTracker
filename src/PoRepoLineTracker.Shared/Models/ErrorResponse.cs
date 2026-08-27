namespace PoRepoLineTracker.Shared.Models;

/// <summary>
/// Standard error payload returned by the API for 4xx/5xx responses.
/// Shared between the API (return type on error paths) and the Blazor WASM client
/// (deserialization target).
///
/// <para><b>Four fields, each with one job.</b> This shape previously carried five, of which
/// <c>Type</c> was never set or read by anything, and the machine-readable code was a property
/// literally named <c>error</c> in lower camel case with a comment explaining that it was spelled
/// that way "to match JSON" — which it did not need to be: the whole contract is serialized with
/// <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>, so every property is camel-cased on
/// the wire anyway. The result was a payload where <c>Title</c>, <c>Detail</c> and <c>error</c>
/// all looked like plausible places to put the message, and different call sites picked
/// differently.</para>
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>Short human-readable summary — the headline, safe to show a user.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable explanation, ideally including what to do about it. Never carries internal
    /// validation detail on a security-relevant failure — see AntiforgeryMiddleware for why.
    /// </summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// Stable machine-readable slug (<c>snake_case</c>), for callers that need to branch on the
    /// KIND of failure rather than parse prose. Serializes as <c>code</c>.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>HTTP status code, repeated in the body so a logged payload is self-describing.</summary>
    public int Status { get; set; }
}
