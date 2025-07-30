using MediatR;
using System.Collections.Generic;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetConfiguredFileExtensionsQuery() : IRequest<IEnumerable<string>>;
}
