using MediatR;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Application.Features.Repositories.Commands;

public record AddMultipleRepositoriesCommand(IEnumerable<BulkRepositoryDto> Repositories, UserId UserId) : IRequest<BulkAddResult>;
