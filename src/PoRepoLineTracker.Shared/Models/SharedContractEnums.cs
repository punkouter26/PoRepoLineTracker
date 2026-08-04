using System.Text.Json.Serialization;

// Rule 2.2 — shared API contract enum: consumed by DTOs in this (leaf) assembly AND by the
// Blazor client. It lives in Shared so it no longer needs a reference to Domain, but keeps the
// PoRepoLineTracker.Domain.Models namespace so every existing using-directive across the
// solution resolves unchanged. Domain references Shared to use it.
namespace PoRepoLineTracker.Domain.Models;

// The generic JsonStringEnumConverter<T>, not the open-ended JsonStringEnumConverter: the
// non-generic form constructs its per-enum converter reflectively at runtime, which defeats the
// source generator and keeps a reflection dependency alive through the trimmer (Rule 1.2).
[JsonConverter(typeof(JsonStringEnumConverter<ChartDisplayMode>))]
public enum ChartDisplayMode
{
    TrueData = 0,
    MovingAverage = 1
}
