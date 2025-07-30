using MediatR;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public record GetRepositoryByOwnerAndNameQuery(string Owner, string RepoName) : IRequest<GitHubRepoStatsDto?>;
}
