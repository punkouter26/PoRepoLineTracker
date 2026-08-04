using System.Text.Json;

namespace PoRepoLineTracker.Shared.Serialization;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance the WASM client passes to every
/// <c>HttpClient</c> JSON call.
///
/// <para>Its resolver is <see cref="AppJsonSerializerContext"/> and nothing else — deliberately
/// no reflection fallback. On the client that is the point: with a reflection resolver in the
/// chain the trimmer must assume any type may be serialized by reflection and keeps the
/// metadata alive, which is what made the published output untrimmable. A type missing from the
/// context now fails loudly at the call site instead of silently reintroducing that dependency.</para>
///
/// <para>The API keeps the default reflection resolver behind the generated one (see
/// <c>InfrastructureServiceExtensions.AddAppJsonSerialization</c>): server-side there is no
/// trimming benefit, and the dev-only client-log endpoint accepts a
/// <c>Dictionary&lt;string, object&gt;</c> that has no source-generable shape.</para>
///
/// <para>Cached in a static because <see cref="JsonSerializerOptions"/> builds an internal type
/// cache on first use; allocating a fresh instance per call would discard it every time.</para>
/// </summary>
public static class AppJsonOptions
{
    /// <summary>Web defaults (camelCase, case-insensitive) resolved purely from source-generated metadata.</summary>
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = AppJsonSerializerContext.Default
    };
}
