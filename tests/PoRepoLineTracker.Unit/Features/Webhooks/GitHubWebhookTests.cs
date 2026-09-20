using FluentAssertions;
using PoRepoLineTracker.API.Features.Webhooks;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Shared.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoRepoLineTracker.Unit.Features.Webhooks;

public class GitHubWebhookTests
{
    private const string TestSecret = "super-secret-webhook-key-12345";

    [Fact]
    public void VerifySignature_with_valid_hmac_returns_true()
    {
        var body = """{"ref":"refs/heads/main","repository":{"name":"PoRepoLineTracker","owner":{"login":"punkouter26"}}}""";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var hash = hmac.ComputeHash(bodyBytes);
        var signature = $"sha256={Convert.ToHexStringLower(hash)}";

        var result = GitHubWebhookEndpoints.VerifySignature(bodyBytes, signature, TestSecret);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_with_uppercase_signature_returns_true()
    {
        var body = """{"action":"push"}""";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var hash = hmac.ComputeHash(bodyBytes);
        var signature = $"sha256={Convert.ToHexString(hash).ToUpperInvariant()}";

        var result = GitHubWebhookEndpoints.VerifySignature(bodyBytes, signature, TestSecret);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_with_tampered_payload_returns_false()
    {
        var originalBody = """{"ref":"refs/heads/main"}""";
        var tamperedBody = """{"ref":"refs/heads/evil"}""";
        var bodyBytes = Encoding.UTF8.GetBytes(originalBody);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var signature = $"sha256={Convert.ToHexStringLower(hmac.ComputeHash(bodyBytes))}";

        var result = GitHubWebhookEndpoints.VerifySignature(Encoding.UTF8.GetBytes(tamperedBody), signature, TestSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_with_missing_or_empty_parameters_returns_false()
    {
        var bytes = Encoding.UTF8.GetBytes("{}");
        GitHubWebhookEndpoints.VerifySignature(bytes, null, TestSecret).Should().BeFalse();
        GitHubWebhookEndpoints.VerifySignature(bytes, "", TestSecret).Should().BeFalse();
        GitHubWebhookEndpoints.VerifySignature(bytes, "invalid-format", TestSecret).Should().BeFalse();
        GitHubWebhookEndpoints.VerifySignature(bytes, "sha256=abcdef", "").Should().BeFalse();
    }

    [Fact]
    public void Deserializes_GitHubWebhookPayload_with_source_generator()
    {
        var json = """
        {
            "ref": "refs/heads/main",
            "repository": {
                "name": "PoRepoLineTracker",
                "default_branch": "main",
                "full_name": "punkouter26/PoRepoLineTracker",
                "owner": {
                    "login": "punkouter26"
                }
            }
        }
        """;

        var payload = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.GitHubWebhookPayload);

        payload.Should().NotBeNull();
        payload!.Ref.Should().Be("refs/heads/main");
        payload.Repository.Should().NotBeNull();
        payload.Repository!.Name.Should().Be("PoRepoLineTracker");
        payload.Repository.DefaultBranch.Should().Be("main");
        payload.Repository.FullName.Should().Be("punkouter26/PoRepoLineTracker");
        payload.Repository.Owner?.Login.Should().Be("punkouter26");
    }
}

