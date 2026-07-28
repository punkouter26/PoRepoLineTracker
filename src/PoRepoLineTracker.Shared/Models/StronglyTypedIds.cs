using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

// Namespace note: these types physically live in .Shared (the leaf assembly, Rule 2.2) because
// both the DTOs here and the Domain entities need them, but they keep the Domain namespace so
// they read as domain vocabulary — the same arrangement already used by SharedContractEnums.
namespace PoRepoLineTracker.Domain.Models;

/// <summary>
/// Identity of a tracked repository (Rule 1.5 — no primitive obsession).
///
/// Why a wrapper rather than <see cref="Guid"/>: every ownership check in the API compares a
/// repository id against a user id. As raw Guids those are the same type, so transposing the two
/// arguments compiles cleanly and fails silently at runtime — an IDOR bug the compiler could have
/// caught. Wrapping them makes the mix-up a build error.
///
/// Implements <see cref="IParsable{TSelf}"/> so minimal-API route parameters bind straight to this
/// type, and carries a converter so JSON stays a bare GUID string — the wire format and the stored
/// Azure Table values are unchanged by the wrapper.
/// </summary>
[JsonConverter(typeof(RepositoryIdJsonConverter))]
public readonly record struct RepositoryId(Guid Value) : IParsable<RepositoryId>
{
    public static readonly RepositoryId Empty = new(Guid.Empty);

    public static RepositoryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static RepositoryId Parse(string s, IFormatProvider? provider = null)
        => new(Guid.Parse(s));

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out RepositoryId result)
    {
        if (Guid.TryParse(s, out var guid))
        {
            result = new RepositoryId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, out RepositoryId result)
        => TryParse(s, null, out result);
}

/// <summary>
/// Identity of an authenticated user (Rule 1.5). See <see cref="RepositoryId"/> for the rationale.
/// </summary>
[JsonConverter(typeof(UserIdJsonConverter))]
public readonly record struct UserId(Guid Value) : IParsable<UserId>
{
    public static readonly UserId Empty = new(Guid.Empty);

    public static UserId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    public static UserId Parse(string s, IFormatProvider? provider = null)
        => new(Guid.Parse(s));

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out UserId result)
    {
        if (Guid.TryParse(s, out var guid))
        {
            result = new UserId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, out UserId result)
        => TryParse(s, null, out result);
}

/// <summary>Serializes <see cref="RepositoryId"/> as a bare GUID string, not as an object.</summary>
public sealed class RepositoryIdJsonConverter : JsonConverter<RepositoryId>
{
    public override RepositoryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, RepositoryId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    public override RepositoryId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(Guid.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, RepositoryId value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value.ToString());
}

/// <summary>Serializes <see cref="UserId"/> as a bare GUID string, not as an object.</summary>
public sealed class UserIdJsonConverter : JsonConverter<UserId>
{
    public override UserId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, UserId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    public override UserId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(Guid.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, UserId value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value.ToString());
}
