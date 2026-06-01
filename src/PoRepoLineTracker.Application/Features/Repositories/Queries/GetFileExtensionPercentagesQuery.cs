using MediatR;
using PoRepoLineTracker.Shared.Models.Dtos;
using System;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetFileExtensionPercentagesQuery(Guid RepositoryId) : IRequest<IEnumerable<FileExtensionPercentageDto>>;
}
