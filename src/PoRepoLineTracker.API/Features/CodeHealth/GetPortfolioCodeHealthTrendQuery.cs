using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.CodeHealth;

/// <summary>Every owned repository's monthly health, for one chart with a line each.</summary>
public record GetPortfolioCodeHealthTrendQuery(UserId UserId) : IRequest<List<CodeHealthTrendDto>>;

/// <summary>
/// <para><b>What this shows that the table cannot.</b> The grid is a snapshot: it says which
/// repositories are healthy today. Plotted together over time it says which are DRIFTING — a
/// repository sitting at 66 and falling matters more than one sitting at 62 and flat, and no
/// column can show that.</para>
///
/// <para><b>Cost.</b> This is the most expensive read in the app on a cold cache: every repository,
/// every month boundary. It is bearable only because each point is memoised per commit and computed
/// at most once ever, so the first run pays and every run after it is storage reads. It is behind
/// its own button for exactly that reason — nobody should navigate into a minute of work.</para>
///
/// <para>Repositories that yield no points are dropped here rather than sent as empty lines: the
/// grid already names them and says why they are unmeasured, and a legend entry with no line drawn
/// invites the reader to hunt for a series that does not exist.</para>
/// </summary>
public sealed class GetPortfolioCodeHealthTrendQueryHandler(
    IRepositoryDataService repositoryDataService,
    IMediator mediator,
    ILogger<GetPortfolioCodeHealthTrendQueryHandler> logger)
    : IRequestHandler<GetPortfolioCodeHealthTrendQuery, List<CodeHealthTrendDto>>
{
    public async Task<List<CodeHealthTrendDto>> Handle(
        GetPortfolioCodeHealthTrendQuery request, CancellationToken cancellationToken)
    {
        var repositories = (await repositoryDataService.GetAllRepositoriesAsync(request.UserId)).ToList();
        var trends = new List<CodeHealthTrendDto>(repositories.Count);

        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var trend = await mediator.Send(new GetCodeHealthTrendQuery(repository.Id), cancellationToken);

                // A repository with a single point draws no line, only a dot — kept, because one
                // dot sitting well below the others is still information the grid does not give.
                if (trend is { Points.Count: > 0 }) trends.Add(trend);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable repository must not lose the chart. It simply has no line.
                logger.LogWarning(ex, "Could not build a health trend for {Owner}/{Name}",
                    repository.Owner, repository.Name);
            }
        }

        // Healthiest last-known first, matching the grid above it, so a reader moving between the
        // two is not silently re-sorted.
        return trends
            .OrderByDescending(t => t.Points[^1].Score)
            .ThenBy(t => t.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
