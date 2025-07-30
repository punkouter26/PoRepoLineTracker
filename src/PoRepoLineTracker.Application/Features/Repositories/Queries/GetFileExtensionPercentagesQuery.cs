using MediatR;
using PoRepoLineTracker.Application.Models;
using System;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetFileExtensionPercentagesQuery(Guid RepositoryId) : IRequest<IEnumerable<FileExtensionPercentageDto>>;
}
