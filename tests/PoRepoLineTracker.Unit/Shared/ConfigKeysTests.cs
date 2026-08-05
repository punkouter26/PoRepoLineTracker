using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Rule 1.5 — guards the constants that replaced the config magic strings. A typo in one of these
/// used to surface as a silently-null configuration value at runtime; here it fails the build's
/// test step instead.
/// </summary>
public class ConfigKeysTests
{
    public static TheoryData<string, string> ExpectedKeys() => new()
    {
        { ConfigKeys.KeyVault.Uri, "KeyVault:Uri" },
        { ConfigKeys.AzureTableStorage.ConnectionString, "AzureTableStorage:ConnectionString" },
        { ConfigKeys.AzureTableStorage.ServiceUrl, "AzureTableStorage:ServiceUrl" },
        { ConfigKeys.AzureTableStorage.RepositoryTableName, "AzureTableStorage:RepositoryTableName" },
        { ConfigKeys.AzureTableStorage.TablesConnectionString, "ConnectionStrings:tables" },
        { ConfigKeys.GitHub.ClientId, "GitHub:ClientId" },
        { ConfigKeys.GitHub.ClientSecret, "GitHub:ClientSecret" },
        { ConfigKeys.GitHub.Pat, "GitHub:PAT" },
        { ConfigKeys.Telemetry.AppInsightsConnectionString, "APPLICATIONINSIGHTS_CONNECTION_STRING" },
        { ConfigKeys.Telemetry.OtlpEndpoint, "OpenTelemetry:OtlpEndpoint" },
        
        { ConfigKeys.ChartSettings.MaxLinesOfCode, "ChartSettings:MaxLinesOfCode" },
    };

    [Theory]
    [MemberData(nameof(ExpectedKeys))]
    public void Constant_MatchesTheKeySpelledInAppSettings(string actual, string expected)
    {
        actual.Should().Be(expected);
    }

    [Fact]
    public void EveryKey_ResolvesAgainstAConfigurationBuiltFromThoseKeys()
    {
        var keys = AllKeys().ToList();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(keys.ToDictionary(k => k, k => (string?)"set"))
            .Build();

        keys.Should().OnlyContain(k => configuration[k] == "set");
    }

    [Fact]
    public void NoTwoConstants_ShareTheSameKey()
    {
        var keys = AllKeys().ToList();

        keys.Should().OnlyHaveUniqueItems(
            "two constants with the same value means one of them is a copy-paste mistake");
    }

    [Fact]
    public void EveryKey_IsNonEmpty()
    {
        AllKeys().Should().OnlyContain(k => !string.IsNullOrWhiteSpace(k));
    }

    private static IEnumerable<string> AllKeys() =>
        typeof(ConfigKeys)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(section => section.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f is { IsLiteral: true, FieldType: { } t } && t == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
