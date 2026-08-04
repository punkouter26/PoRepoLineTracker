using System.Net.Http.Json;
using PoRepoLineTracker.Shared.Serialization;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// <c>HttpClient</c> JSON helpers bound to <see cref="AppJsonOptions.Default"/>.
///
/// <para>The framework's own <c>GetFromJsonAsync</c>/<c>ReadFromJsonAsync</c> overloads fall back
/// to the ambient reflection-based serializer when no options argument is supplied, which is the
/// one thing the trimmed WASM build cannot tolerate (Rule 1.2). Passing the options at each of
/// the ~23 call sites would work, but it is silently optional — a new call written without them
/// compiles and then fails only at runtime, in the browser.</para>
///
/// <para>Routing every client request through these wrappers instead makes the source-generated
/// path the default and the reflection path unreachable: there is no overload here that omits
/// the options.</para>
/// </summary>
internal static class AppHttpJsonExtensions
{
    /// <summary>GET and deserialize using source-generated metadata.</summary>
    public static Task<T?> GetAppJsonAsync<T>(this HttpClient client, string requestUri, CancellationToken cancellationToken = default)
        => client.GetFromJsonAsync<T>(requestUri, AppJsonOptions.Default, cancellationToken);

    /// <summary>Deserialize a response body using source-generated metadata.</summary>
    public static Task<T?> ReadAppJsonAsync<T>(this HttpContent content, CancellationToken cancellationToken = default)
        => content.ReadFromJsonAsync<T>(AppJsonOptions.Default, cancellationToken);

    /// <summary>PUT a body serialized with source-generated metadata.</summary>
    public static Task<HttpResponseMessage> PutAppJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken cancellationToken = default)
        => client.PutAsJsonAsync(requestUri, value, AppJsonOptions.Default, cancellationToken);

    /// <summary>POST a body serialized with source-generated metadata.</summary>
    public static Task<HttpResponseMessage> PostAppJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken cancellationToken = default)
        => client.PostAsJsonAsync(requestUri, value, AppJsonOptions.Default, cancellationToken);
}
