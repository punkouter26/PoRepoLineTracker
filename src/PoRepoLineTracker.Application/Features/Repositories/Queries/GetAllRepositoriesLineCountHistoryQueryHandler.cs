using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Application.Models;
using PoRepoLineTracker.Domain.Models; // Added for CommitLineCount
using System.Collections.Generic;
using System.Linq; // Added for LINQ operations
using System.Threading;
using System.Threading.Tasks;
using System; // Added for DateTimeOffset

namespace PoRepoLineTracker.Application.Features.Repositories.Queries
{
    public class GetAllRepositoriesLineCountHistoryQueryHandler : IRequestHandler<GetAllRepositoriesLineCountHistoryQuery, IEnumerable<RepositoryLineCountHistoryDto>>
    {
        private readonly IRepositoryDataService _repositoryDataService;

        public GetAllRepositoriesLineCountHistoryQueryHandler(IRepositoryDataService repositoryDataService)
        {
            _repositoryDataService = repositoryDataService;
        }

        public async Task<IEnumerable<RepositoryLineCountHistoryDto>> Handle(GetAllRepositoriesLineCountHistoryQuery request, CancellationToken cancellationToken)
        {
            var allRepositories = (await _repositoryDataService.GetAllRepositoriesAsync(request.UserId)).ToList();

            // Fetch all repos' commit data in parallel — eliminates N sequential Azure Table round-trips
            var fetchTasks = allRepositories.Select(repo =>
                _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repo.Id)
                    .ContinueWith(t => (repo, commits: t.Result), TaskContinuationOptions.OnlyOnRanToCompletion));

            var results = await Task.WhenAll(fetchTasks);

            return results.Select(r =>
            {
                var dailyLineCounts = r.commits
                    .Where(clc => clc.CommitDate >= DateTimeOffset.UtcNow.AddDays(-request.Days))
                    .GroupBy(clc => clc.CommitDate.Date)
                    .Select(g => new DailyLineCountDto
                    {
                        Date = g.Key,
                        // TotalLines is a per-commit snapshot; use the last commit of the day
                        TotalLines = g.OrderByDescending(clc => clc.CommitDate).First().TotalLines,
                        TotalLinesAdded = g.Sum(clc => clc.LinesAdded),
                        TotalLinesDeleted = g.Sum(clc => clc.LinesRemoved),
                        TotalLinesChanged = g.Sum(clc => clc.LinesAdded + clc.LinesRemoved)
                    })
                    .OrderBy(dlc => dlc.Date)
                    .ToList();

                return new RepositoryLineCountHistoryDto
                {
                    RepositoryId = r.repo.Id,
                    RepositoryName = r.repo.Name,
                    Owner = r.repo.Owner,
                    DailyLineCounts = dailyLineCounts
                };
            });
        }
    }
}
