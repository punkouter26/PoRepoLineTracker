using MediatR;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public class AddLocalRepositoryCommand : IRequest<PoRepoLineTracker.Domain.Models.GitHubRepository>
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string LocalGitPath { get; set; } = string.Empty;
}