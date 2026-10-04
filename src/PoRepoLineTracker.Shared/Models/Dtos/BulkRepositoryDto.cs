using System.Text.Json.Serialization;

namespace PoRepoLineTracker.Shared.Models.Dtos;

public class BulkRepositoryDto
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repoName")]
    public string RepoName { get; set; } = string.Empty;

    // No CloneUrl. The server derives it from Owner/RepoName (GitHubRepository.GitHubCloneUrl):
    // a caller-supplied URL was cloned verbatim with the user's GitHub token embedded in it, so
    // any authenticated user could point the server — and the token — at a host of their choosing.
}
