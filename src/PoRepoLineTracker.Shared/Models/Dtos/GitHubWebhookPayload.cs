using System.Text.Json.Serialization;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Minimal payload shape for incoming GitHub push webhooks.
/// </summary>
public sealed record GitHubWebhookPayload
{
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    [JsonPropertyName("repository")]
    public GitHubWebhookRepository? Repository { get; init; }
}

public sealed record GitHubWebhookRepository
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("owner")]
    public GitHubWebhookOwner? Owner { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; init; }
}

public sealed record GitHubWebhookOwner
{
    [JsonPropertyName("login")]
    public string? Login { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

