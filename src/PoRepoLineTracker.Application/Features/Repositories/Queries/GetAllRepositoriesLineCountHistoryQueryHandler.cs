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
            var allRepositories = await _repositoryDataService.GetAllRepositoriesAsync(request.UserId);
            var result = new List<RepositoryLineCountHistoryDto>();

            foreach (var repo in allRepositories)
            {
                var commitLineCounts = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(repo.Id);

                var dailyLineCounts = commitLineCounts
                    .Where(clc => clc.CommitDate >= DateTimeOffset.UtcNow.AddDays(-request.Days))
                    .GroupBy(clc => clc.CommitDate.Date)
                    .Select(g => new DailyLineCountDto
                    {
                        Date = g.Key,
                        TotalLinesAdded = g.Sum(clc => clc.LinesAdded),
                        TotalLinesDeleted = g.Sum(clc => clc.LinesRemoved),
                        TotalLinesChanged = g.Sum(clc => clc.LinesAdded + clc.LinesRemoved)
                    })
                    .OrderBy(dlc => dlc.Date)
                    .ToList();

                result.Add(new RepositoryLineCountHistoryDto
                {
                    RepositoryId = repo.Id,
                    RepositoryName = repo.Name,
                    Owner = repo.Owner,
                    DailyLineCounts = dailyLineCounts
                });
            }

            return result;
        }
    }
}
