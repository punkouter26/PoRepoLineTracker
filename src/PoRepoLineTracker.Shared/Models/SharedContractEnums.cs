using System.Text.Json.Serialization;

// Rule 2.2 — these enums are part of the shared API contract: they are consumed by DTOs in
// this (leaf) assembly AND by the Blazor client. They live in Shared so it no longer needs a
// reference to Domain, but keep the PoRepoLineTracker.Domain.Models namespace so every existing
// using-directive across the solution resolves unchanged. Domain references Shared to use them.
namespace PoRepoLineTracker.Domain.Models;

/// <summary>Metrics that can be monitored by alert rules.</summary>
public enum AlertMetric
{
    /// <summary>AI detection percentage (0-100).</summary>
    AiPercentage = 0,

    /// <summary>Weekly line count change (percentage).</summary>
    WeeklyLineChange = 1,

    /// <summary>Total lines of code.</summary>
    TotalLines = 2,

    /// <summary>Number of commits in the last 7 days.</summary>
    WeeklyCommitCount = 3
}

/// <summary>Comparison operators for alert thresholds.</summary>
public enum AlertOperator
{
    GreaterThan = 0,
    LessThan = 1,
    GreaterThanOrEqual = 2,
    LessThanOrEqual = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChartDisplayMode
{
    TrueData = 0,
    MovingAverage = 1
}
