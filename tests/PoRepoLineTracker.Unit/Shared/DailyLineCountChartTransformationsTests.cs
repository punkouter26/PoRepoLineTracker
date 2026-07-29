using FluentAssertions;

namespace PoRepoLineTracker.Unit;

public class DailyLineCountChartTransformationsTests
{
    [Fact]
    public void TransformForDisplay_TrueDataMode_ReturnsOrderedRawPoints()
    {
        var rawPoints = new List<DailyLineCountDto>
        {
            new() { Date = new DateTime(2026, 4, 3), TotalLines = 300 },
            new() { Date = new DateTime(2026, 4, 1), TotalLines = 100 },
            new() { Date = new DateTime(2026, 4, 2), TotalLines = 200 }
        };

        var transformed = DailyLineCountChartTransformations.TransformForDisplay(rawPoints, ChartDisplayMode.TrueData);

        transformed.Select(point => point.TotalLines).Should().Equal(100, 200, 300);
        transformed.Select(point => point.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public void TransformForDisplay_MovingAverageMode_FillsMissingDaysAndCalculatesRollingAverage()
    {
        var rawPoints = new List<DailyLineCountDto>
        {
            new() { Date = new DateTime(2026, 4, 1), TotalLines = 100 },
            new() { Date = new DateTime(2026, 4, 3), TotalLines = 400 }
        };

        var transformed = DailyLineCountChartTransformations.TransformForDisplay(rawPoints, ChartDisplayMode.MovingAverage, movingAverageWindowDays: 3);

        transformed.Select(point => point.Date).Should().Equal(
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 2),
            new DateTime(2026, 4, 3));
        transformed.Select(point => point.TotalLines).Should().Equal(100, 100, 200);
    }
}