using MediatR;
using Microsoft.AspNetCore.Mvc;
using PoRepoLineTracker.API.Features.Repositories;
using PoRepoLineTracker.API.Middleware;
using PoRepoLineTracker.API.Storage;
using PoRepoLineTracker.Shared.Models;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Shared.Serialization;
using Serilog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoRepoLineTracker.API.Features.Webhooks;

public static class GitHubWebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/webhooks")
            .WithTags("Webhooks");

        group.MapPost("/github", async (
            HttpContext context,
            IConfiguration config,
            IRepositoryDataService repoDataService,
            IServiceScopeFactory scopeFactory) =>
        {
            var secret = config[ConfigKeys.GitHub.WebhookSecret];
            if (string.IsNullOrEmpty(secret))
            {
                Log.Warning("GitHub webhook rejected: GitHub:WebhookSecret is not configured");
                return Results.Unauthorized();
            }

            var signatureHeader = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (string.IsNullOrEmpty(signatureHeader))
            {
                Log.Warning("GitHub webhook rejected: missing X-Hub-Signature-256 header");
                return Results.Unauthorized();
            }

            // Read raw body bytes for HMAC verification
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();
            var bodyBytes = Encoding.UTF8.GetBytes(rawBody);

            if (!VerifySignature(bodyBytes, signatureHeader, secret))
            {
                Log.Warning("GitHub webhook rejected: signature mismatch");
                return Results.Unauthorized();
            }

            GitHubWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize(rawBody, AppJsonSerializerContext.Default.GitHubWebhookPayload);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "GitHub webhook rejected: malformed JSON payload");
                return Results.BadRequest(new { message = "Malformed JSON payload" });
            }

            if (payload?.Repository is null)
            {
                return Results.Ok(new { message = "Ignored: missing repository payload" });
            }

            var repo = payload.Repository;
            var owner = repo.Owner?.Login ?? repo.Owner?.Name;
            var name = repo.Name;

            if (string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repo.FullName) && repo.FullName.Contains('/'))
            {
                var parts = repo.FullName.Split('/');
                owner = parts[0];
                name = parts[1];
            }

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name))
            {
                return Results.Ok(new { message = "Ignored: missing owner or repository name" });
            }

            // If default_branch is specified, only trigger on pushes to that branch
            if (!string.IsNullOrEmpty(repo.DefaultBranch) && !string.IsNullOrEmpty(payload.Ref))
            {
                var expectedRef = $"refs/heads/{repo.DefaultBranch}";
                if (!string.Equals(payload.Ref, expectedRef, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("GitHub webhook ignored push to non-default branch {Ref} for {Owner}/{Name}",
                        payload.Ref, owner, name);
                    return Results.Ok(new { message = $"Ignored push to non-default branch {payload.Ref}" });
                }
            }

            var existingRepo = await repoDataService.FindRepositoryByOwnerAndNameAsync(owner, name);
            if (existingRepo is null)
            {
                Log.Information("GitHub webhook received push for untracked repository: {Owner}/{Name}", owner, name);
                return Results.Ok(new { message = "Repository is not tracked" });
            }

            Log.Information("GitHub webhook verified push for tracked repository {Owner}/{Name} ({RepoId}). Queuing analysis...",
                owner, name, existingRepo.Id);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                    await mediator.Send(new AnalyzeRepositoryCommitsCommand(existingRepo.Id));
                    Log.Information("Background webhook analysis finished for {RepoId}", existingRepo.Id);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background webhook analysis failed for repo {RepoId}: {Message}", existingRepo.Id, ex.Message);
                }
            });

            return Results.Accepted(null, new { message = "Analysis queued", repositoryId = existingRepo.Id.Value });
        })
        .AllowAnonymous()
        .WithMetadata(new SkipAntiforgeryAttribute("GitHub webhooks authenticate via HMAC-SHA256 signature header, not browser antiforgery token"))
        .WithName("GitHubWebhook");
    }

    /// <summary>
    /// Validates GitHub's HMAC-SHA256 signature against the raw body bytes and configured secret.
    /// Uses constant-time comparison to prevent timing side-channel attacks.
    /// </summary>
    public static bool VerifySignature(ReadOnlySpan<byte> bodyBytes, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(secret))
            return false;

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedHex = signatureHeader[prefix.Length..];
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(bodyBytes.ToArray());
        var computedHex = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant()));
    }
}

