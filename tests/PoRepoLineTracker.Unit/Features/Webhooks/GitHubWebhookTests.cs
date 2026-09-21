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
    public void VerifySignature_validates_correct_signatures_and_rejects_tampered_or_malformed()
    {
        var body = """{"ref":"refs/heads/main","repository":{"name":"PoRepoLineTracker","owner":{"login":"punkouter26"}}}""";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var hash = hmac.ComputeHash(bodyBytes);
        var lowerSig = $"sha256={Convert.ToHexStringLower(hash)}";
        var upperSig = $"sha256={Convert.ToHexString(hash).ToUpperInvariant()}";

        GitHubWebhookEndpoints.VerifySignature(bodyBytes, lowerSig, TestSecret).Should().BeTrue();
        GitHubWebhookEndpoints.VerifySignature(bodyBytes, upperSig, TestSecret).Should().BeTrue();

        var tamperedBody = """{"ref":"refs/heads/evil"}""";
        GitHubWebhookEndpoints.VerifySignature(Encoding.UTF8.GetBytes(tamperedBody), lowerSig, TestSecret).Should().BeFalse();

        var emptyBytes = Encoding.UTF8.GetBytes("{}");
        GitHubWebhookEndpoints.VerifySignature(emptyBytes, null, TestSecret).Should().BeFalse();
        GitHubWebhookEndpoints.VerifySignature(emptyBytes, "", TestSecret).Should().BeFalse();
        GitHubWebhookEndpoints.VerifySignature(emptyBytes, "invalid-format", TestSecret).Should().BeFalse();
        GitHubWebhookEndpoints.VerifySignature(emptyBytes, "sha256=abcdef", "").Should().BeFalse();
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

