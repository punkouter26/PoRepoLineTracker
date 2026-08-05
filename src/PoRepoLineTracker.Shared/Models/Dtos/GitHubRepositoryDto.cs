using PoRepoLineTracker.Shared.Domain;
namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// Wire shape for a tracked repository. Property names mirror the Domain entity so the
/// JSON contract is unchanged; keeping it in Shared lets the leaf assembly stay free of
/// a Domain reference (Rule 2.2).
/// </summary>
public sealed class GitHubRepositoryDto
{
    public RepositoryId Id { get; set; }
    public UserId UserId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public DateTime? LastAnalyzedCommitDate { get; set; }
    public string LocalPath { get; set; } = string.Empty;
}
