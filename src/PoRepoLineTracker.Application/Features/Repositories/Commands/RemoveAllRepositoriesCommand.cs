using MediatR;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

/// <summary>
/// Command Pattern: Command to remove all repositories for a user, their commit data, and local file system data.
/// This is a destructive operation that cleans up all repository-related data for the specified user.
/// </summary>
public record RemoveAllRepositoriesCommand(Guid UserId) : IRequest<Unit>;
