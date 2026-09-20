using Microsoft.AspNetCore.Mvc;
using PoRepoLineTracker.API.Storage;
using PoRepoLineTracker.Shared.Domain;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Shared.Serialization;
using System.Text;

namespace PoRepoLineTracker.API.Features.Repositories;

public static class PortfolioExportEndpoints
{
    public static void MapPortfolioExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/repositories/export", async (
            [FromQuery] string? format,
            HttpContext ctx,
            IRepositoryDataService repoDataService) =>
        {
            if (!ctx.User.TryGetUserId(out var userId))
                return Results.Unauthorized();

            var reposWithCommits = await repoDataService.GetAllRepositoriesWithCommitsAsync(userId);
            var rows = reposWithCommits.Select(pair =>
            {
                var total = pair.Commits.Count > 0 ? RepositoryTotals.LatestTotalLines(pair.Commits) : (int?)null;
                return new PortfolioExportRowDto
                {
                    Owner = pair.Repository.Owner,
                    Name = pair.Repository.Name,
                    TotalLines = total,
                    LastAnalyzedUtc = pair.Repository.LastAnalyzedCommitDate,
                    CloneUrl = pair.Repository.CloneUrl
                };
            }).OrderBy(r => r.Owner).ThenBy(r => r.Name).ToList();

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csv = BuildCsv(rows);
                var bytes = Encoding.UTF8.GetBytes(csv);
                return Results.File(bytes, "text/csv", $"porepo-portfolio-{DateTime.UtcNow:yyyyMMdd}.csv");
            }

            return Results.Json(rows, AppJsonSerializerContext.Default.ListPortfolioExportRowDto);
        })
        .RequireAuthorization()
        .WithName("ExportPortfolio");
    }

    public static string BuildCsv(IEnumerable<PortfolioExportRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Owner,Repository,TotalLines,LastAnalyzedUtc,CloneUrl");
        foreach (var r in rows)
        {
            var owner = EscapeCsv(r.Owner);
            var name = EscapeCsv(r.Name);
            var lines = r.TotalLines.HasValue ? r.TotalLines.Value.ToString() : "";
            var date = r.LastAnalyzedUtc.HasValue ? r.LastAnalyzedUtc.Value.ToString("o") : "";
            var url = EscapeCsv(r.CloneUrl);
            sb.AppendLine($"{owner},{name},{lines},{date},{url}");
        }
        return sb.ToString();
    }

    public static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
