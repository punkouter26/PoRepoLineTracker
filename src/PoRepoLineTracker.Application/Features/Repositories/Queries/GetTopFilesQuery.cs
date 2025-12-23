using MediatR;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public record GetTopFilesQuery(Guid RepositoryId, int Count = 5) : IRequest<IEnumerable<TopFileDto>>;
