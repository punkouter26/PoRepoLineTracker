namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Category of AI model backend (Rule 14). The home-page model selector groups options
/// under these three headings.
/// </summary>
public enum AiModelCategory
{
    /// <summary>Remote cloud models hosted in Azure OpenAI.</summary>
    Remote,

    /// <summary>Models that load and run inside the user's web browser (WASM).</summary>
    Browser,

    /// <summary>Local Ollama models — only offered when the app runs locally.</summary>
    Ollama
}

/// <summary>
/// A selectable AI model returned by <c>GET /api/ai-models</c>.
/// </summary>
/// <param name="Id">Stable identifier persisted as the user's selection.</param>
/// <param name="DisplayName">Human-readable label shown in the dropdown.</param>
/// <param name="Category">Group the model belongs to.</param>
public record AiModelDto(string Id, string DisplayName, AiModelCategory Category);
