using MediatR;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

/// <summary>
/// Command Pattern: Command to remove all repositories, their commit data, and local file system data.
/// This is a destructive operation that cleans up all repository-related data.
/// </summary>
public record RemoveAllRepositoriesCommand : IRequest<Unit>;
