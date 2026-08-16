using System.Reflection;
using FluentAssertions;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Guards the constants that replaced the config magic strings. A typo in one of these
/// used to surface as a silently-null configuration value at runtime; here it fails the build's
/// test step instead.
/// </summary>
public class ConfigKeysTests
{
    [Fact]
    public void NoTwoConstants_ShareTheSameKey()
    {
        var keys = AllKeys().ToList();

        keys.Should().OnlyHaveUniqueItems(
            "two constants with the same value means one of them is a copy-paste mistake");
    }

    private static IEnumerable<string> AllKeys() =>
        typeof(ConfigKeys)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(section => section.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f is { IsLiteral: true, FieldType: { } t } && t == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
