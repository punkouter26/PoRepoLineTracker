using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// GoF Strategy Pattern: Transforms raw daily line-count data for chart display.
/// Supports TrueData (raw) and MovingAverage (rolling window) display modes.
/// Shared between the Blazor WASM client and unit tests.
/// </summary>
public static class DailyLineCountChartTransformations
{
    public static IReadOnlyList<DailyLineCountDto> TransformForDisplay(
        IEnumerable<DailyLineCountDto>? lineCounts,
        ChartDisplayMode chartDisplayMode,
        int movingAverageWindowDays = 90)
    {
        var orderedLineCounts = (lineCounts ?? [])
            .OrderBy(point => point.Date)
            .ToList();

        if (orderedLineCounts.Count == 0 || chartDisplayMode == ChartDisplayMode.TrueData)
        {
            return orderedLineCounts;
        }

        return CalculateRollingAverage(orderedLineCounts, movingAverageWindowDays);
    }

    private static IReadOnlyList<DailyLineCountDto> CalculateRollingAverage(
        IReadOnlyList<DailyLineCountDto> orderedLineCounts,
        int movingAverageWindowDays)
    {
        var normalizedDailySnapshots = NormalizeToDailySnapshots(orderedLineCounts);
        var rollingWindow = new Queue<int>();
        var rollingAveragePoints = new List<DailyLineCountDto>(normalizedDailySnapshots.Count);
        long rollingSum = 0;

        foreach (var point in normalizedDailySnapshots)
        {
            rollingWindow.Enqueue(point.TotalLines);
            rollingSum += point.TotalLines;

            if (rollingWindow.Count > movingAverageWindowDays)
            {
                rollingSum -= rollingWindow.Dequeue();
            }

            var averageTotalLines = (int)Math.Round(
                rollingSum / (double)rollingWindow.Count,
                MidpointRounding.AwayFromZero);

            rollingAveragePoints.Add(new DailyLineCountDto
            {
                Date = point.Date,
                TotalLines = averageTotalLines,
                TotalLinesAdded = point.TotalLinesAdded,
                TotalLinesDeleted = point.TotalLinesDeleted,
                TotalLinesChanged = point.TotalLinesChanged,
                LinesByFileType = new Dictionary<string, int>(point.LinesByFileType),
                CommitCount = point.CommitCount
            });
        }

        return rollingAveragePoints;
    }

    private static List<DailyLineCountDto> NormalizeToDailySnapshots(IReadOnlyList<DailyLineCountDto> orderedLineCounts)
    {
        var snapshotsByDate = orderedLineCounts
            .GroupBy(point => point.Date.Date)
            .ToDictionary(group => group.Key, group => group.Last());

        var normalizedPoints = new List<DailyLineCountDto>();
        var currentSnapshot = snapshotsByDate[orderedLineCounts[0].Date.Date];

        for (var date = orderedLineCounts[0].Date.Date; date <= orderedLineCounts[^1].Date.Date; date = date.AddDays(1))
        {
            if (snapshotsByDate.TryGetValue(date, out var snapshotForDate))
            {
                currentSnapshot = snapshotForDate;
            }

            normalizedPoints.Add(new DailyLineCountDto
            {
                Date = date,
                TotalLines = currentSnapshot.TotalLines,
                TotalLinesAdded = snapshotsByDate.TryGetValue(date, out var exactSnapshot) ? exactSnapshot.TotalLinesAdded : 0,
                TotalLinesDeleted = snapshotsByDate.TryGetValue(date, out exactSnapshot) ? exactSnapshot.TotalLinesDeleted : 0,
                TotalLinesChanged = snapshotsByDate.TryGetValue(date, out exactSnapshot) ? exactSnapshot.TotalLinesChanged : 0,
                LinesByFileType = snapshotsByDate.TryGetValue(date, out exactSnapshot)
                    ? new Dictionary<string, int>(exactSnapshot.LinesByFileType)
                    : new Dictionary<string, int>(),
                CommitCount = snapshotsByDate.TryGetValue(date, out exactSnapshot) ? exactSnapshot.CommitCount : 0
            });
        }

        return normalizedPoints;
    }
}
