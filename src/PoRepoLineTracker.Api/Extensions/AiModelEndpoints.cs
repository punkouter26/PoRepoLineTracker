using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Api.Extensions;

/// <summary>
/// Rule 14 — exposes the catalog of selectable AI models, grouped into three categories
/// (Remote / Browser / Ollama) for the home-page model selector.
/// </summary>
internal static class AiModelEndpoints
{
    internal static void MapAiModelEndpoints(this WebApplication app)
    {
        app.MapGet("/api/ai-models", () =>
        {
            // Ollama models run on the developer's own machine, so they are only offered when the
            // app is NOT hosted in Azure App Service. App Service always sets WEBSITE_SITE_NAME,
            // which is the most reliable "am I running in the cloud?" signal.
            var runningLocally = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

            var models = new List<AiModelDto>
            {
                // 1. Remote — Azure OpenAI
                new("azure-gpt-4o", "GPT-4o (Azure OpenAI)", AiModelCategory.Remote),
                new("azure-gpt-4o-mini", "GPT-4o mini (Azure OpenAI)", AiModelCategory.Remote),
                new("azure-gpt-35-turbo", "GPT-3.5 Turbo (Azure OpenAI)", AiModelCategory.Remote),

                // 2. Browser — in-browser WASM models
                new("browser-phi-3-mini", "Phi-3 mini (in browser)", AiModelCategory.Browser),
                new("browser-llama-3.2-1b", "Llama 3.2 1B (in browser)", AiModelCategory.Browser),
            };

            // 3. Ollama — local only
            if (runningLocally)
            {
                models.Add(new("ollama-llama3.2", "Llama 3.2 (Ollama)", AiModelCategory.Ollama));
                models.Add(new("ollama-phi3", "Phi-3 (Ollama)", AiModelCategory.Ollama));
                models.Add(new("ollama-qwen2.5", "Qwen 2.5 (Ollama)", AiModelCategory.Ollama));
            }

            return Results.Ok(models);
        })
        .AllowAnonymous()
        .WithName("GetAiModels");
    }
}
