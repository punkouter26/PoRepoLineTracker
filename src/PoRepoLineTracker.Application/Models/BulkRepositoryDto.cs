using System.Text.Json.Serialization;

namespace PoRepoLineTracker.Application.Models;

public class BulkRepositoryDto
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repoName")]
    public string RepoName { get; set; } = string.Empty;

    [JsonPropertyName("cloneUrl")]
    public string CloneUrl { get; set; } = string.Empty;
}
