using System.Text.Json.Serialization;

// Rule 2.2 — shared API contract enum: consumed by DTOs in this (leaf) assembly AND by the
// Blazor client. It lives in Shared so it no longer needs a reference to Domain, but keeps the
// PoRepoLineTracker.Domain.Models namespace so every existing using-directive across the
// solution resolves unchanged. Domain references Shared to use it.
namespace PoRepoLineTracker.Domain.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChartDisplayMode
{
    TrueData = 0,
    MovingAverage = 1
}
