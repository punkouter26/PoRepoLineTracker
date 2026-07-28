using PoRepoLineTracker.Domain.Models;
using MediatR;
using PoRepoLineTracker.Shared.Models.Dtos;
using System;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetFileExtensionPercentagesQuery(RepositoryId RepositoryId) : IRequest<IEnumerable<FileExtensionPercentageDto>>;
}
