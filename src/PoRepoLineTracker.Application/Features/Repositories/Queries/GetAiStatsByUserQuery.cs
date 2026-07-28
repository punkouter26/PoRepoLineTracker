using PoRepoLineTracker.Domain.Models;
using MediatR;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models.Dtos;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Application.Features.Repositories.Queries;

public record GetAiStatsByUserQuery(RepositoryId RepositoryId, int Days = 365) : IRequest<IEnumerable<AiStatsByUserDto>>;

public class GetAiStatsByUserQueryHandler : IRequestHandler<GetAiStatsByUserQuery, IEnumerable<AiStatsByUserDto>>
{
    private readonly IRepositoryDataService _repositoryDataService;
    private readonly ILogger<GetAiStatsByUserQueryHandler> _logger;

    public GetAiStatsByUserQueryHandler(
        IRepositoryDataService repositoryDataService,
        ILogger<GetAiStatsByUserQueryHandler> logger)
    {
        _repositoryDataService = repositoryDataService;
        _logger = logger;
    }

    public async Task<IEnumerable<AiStatsByUserDto>> Handle(GetAiStatsByUserQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting AI stats by user for repository {RepositoryId} for last {Days} days",
            request.RepositoryId, request.Days);

        var commits = await _repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId);

        var cutoffDate = DateTime.UtcNow.AddDays(-request.Days);
        var filteredCommits = commits
            .Where(c => c.CommitDate >= cutoffDate)
            .OrderBy(c => c.CommitDate)
            .ToList();

        _logger.LogInformation("Found {Count} commits in date range for AI analysis", filteredCommits.Count);

        // Group by author
        var groupedByAuthor = filteredCommits
            .GroupBy(c => string.IsNullOrEmpty(c.AuthorName) ? c.AuthorEmail : c.AuthorName)
            .Select(g =>
            {
                var commitsWithAiData = g.Where(c => c.AiPercentage > 0).ToList();

                // For commits without AI data, estimate based on historical patterns
                var avgAiPercentage = commitsWithAiData.Any()
                    ? commitsWithAiData.Average(c => c.AiPercentage)
                    : 0.0;

                var result = new AiStatsByUserDto
                {
                    AuthorName = g.Key,
                    AverageAiPercentage = Math.Round(avgAiPercentage, 2),
                    TotalCommits = g.Count(),
                    CommitHistory = g.Select(c => new UserAiPercentagePerCommitDto
                    {
                        Date = c.CommitDate,
                        CommitSha = c.CommitSha,
                        AiPercentage = c.AiPercentage > 0 ? c.AiPercentage : avgAiPercentage,
                        LinesAdded = c.LinesAdded
                    }).OrderBy(c => c.Date).ToList()
                };

                return result;
            })
            .OrderByDescending(r => r.TotalCommits)
            .ToList();

        _logger.LogInformation("Found {Count} authors with AI stats", groupedByAuthor.Count);
        return groupedByAuthor;
    }
}
