using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// <c>HttpClient</c> JSON helpers that take source-generated <see cref="JsonTypeInfo{T}"/>.
///
/// <para><b>Why JsonTypeInfo and not JsonSerializerOptions.</b> The options-taking overloads are
/// annotated <c>RequiresUnreferencedCode</c> unconditionally — the annotation is on the method,
/// so the trim analyzer flags them (IL2026) even when the options carry nothing but a generated
/// resolver, because it cannot prove that at the call site. Passing the metadata object directly
/// is the only form the analyzer accepts, and with
/// <c>TreatWarningsAsErrors</c> plus <c>EnableTrimAnalyzer</c> (Rule 1.2/1.3) it is the only form
/// that builds.</para>
///
/// <para>The practical effect is that the type argument is now inferred from the metadata rather
/// than written by hand: <c>GetAppJsonAsync(url, AppJsonSerializerContext.Default.ListTopFileDto)</c>.
/// A type missing from the context has no property to pass, so it fails at compile time instead
/// of at runtime in the browser.</para>
/// </summary>
internal static class AppHttpJsonExtensions
{
    /// <summary>GET and deserialize using source-generated metadata.</summary>
    public static Task<T?> GetAppJsonAsync<T>(
        this HttpClient client, string requestUri, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => client.GetFromJsonAsync(requestUri, typeInfo, cancellationToken);

    /// <summary>Deserialize a response body using source-generated metadata.</summary>
    public static Task<T?> ReadAppJsonAsync<T>(
        this HttpContent content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => content.ReadFromJsonAsync(typeInfo, cancellationToken);

    /// <summary>PUT a body serialized with source-generated metadata.</summary>
    public static Task<HttpResponseMessage> PutAppJsonAsync<T>(
        this HttpClient client, string requestUri, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => client.PutAsJsonAsync(requestUri, value, typeInfo, cancellationToken);

    /// <summary>POST a body serialized with source-generated metadata.</summary>
    public static Task<HttpResponseMessage> PostAppJsonAsync<T>(
        this HttpClient client, string requestUri, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => client.PostAsJsonAsync(requestUri, value, typeInfo, cancellationToken);
}
