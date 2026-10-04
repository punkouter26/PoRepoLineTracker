using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace PoRepoLineTracker.API.Analysis;

/// <summary>
/// The one place this app talks to Claude: a single request that must come back as JSON matching
/// a schema. Used by the digest narrative and the natural-language grid filter.
///
/// <para><b>Optional by construction.</b> With no <c>Anthropic:ApiKey</c> configured
/// <see cref="IsAvailable"/> is false, no client is built and nothing leaves the process — the
/// app boots and runs exactly as it did before this existed (SPEC §12.8).</para>
///
/// <para><b>What it is given.</b> Callers pass numbers and the user's own question — never
/// repository names, author names or commit messages, which are text an outsider can write and
/// would be the route for a prompt injection. The reply is schema-constrained, and callers
/// validate it again before using it.</para>
/// </summary>
public sealed class ClaudeAssistant
{
    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly ILogger<ClaudeAssistant> _logger;

    public ClaudeAssistant(IConfiguration configuration, ILogger<ClaudeAssistant> logger)
    {
        _logger = logger;
        _model = configuration[ConfigKeys.Anthropic.Model] is { Length: > 0 } model ? model : "claude-opus-5-5";

        var apiKey = configuration[ConfigKeys.Anthropic.ApiKey];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // A short timeout: both callers decorate a page that is already complete without
            // them, so a slow answer is worth less than no answer.
            _client = new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(30) };
        }
    }

    public bool IsAvailable => _client is not null;

    /// <summary>
    /// Sends one request and returns the reply parsed as JSON, or null when the feature is off,
    /// the model declined, or the call failed. Never throws for an API failure: every caller's
    /// correct response to "no answer" is to show nothing.
    /// </summary>
    public async Task<JsonElement?> AskJsonAsync(
        string system, string user, Dictionary<string, JsonElement> schema, CancellationToken cancellationToken)
    {
        if (_client is null) return null;

        try
        {
            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                // Room for the model's thinking as well as a reply of a few hundred tokens.
                MaxTokens = 4000,
                System = system,
                Messages = [new() { Role = Role.User, Content = user }],
                OutputConfig = new BetaOutputConfig
                {
                    // Both tasks are simple rewrites; low effort keeps them quick and cheap.
                    Effort = Effort.Low,
                    Format = new BetaJsonOutputFormat { Schema = schema },
                },
                // If the safety classifiers decline, the request is re-served by the fallback
                // model inside the same call rather than failing. "default" lets the API choose
                // the fallback by refusal category, so there is no model list to maintain here.
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
            }, cancellationToken: cancellationToken);

            if (response.StopReason == "refusal")
            {
                _logger.LogInformation("Claude declined a request ({Category})", response.StopDetails?.Category);
                return null;
            }

            var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : string.Empty));
            return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Claude request failed; the feature shows nothing");
            return null;
        }
    }

    /// <summary>A JSON-schema object with every listed property required and nothing else allowed.</summary>
    public static Dictionary<string, JsonElement> ObjectSchema(object properties, params string[] required) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(properties),
        ["required"] = JsonSerializer.SerializeToElement(required),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
