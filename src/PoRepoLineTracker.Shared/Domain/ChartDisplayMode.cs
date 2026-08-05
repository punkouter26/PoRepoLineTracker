using System.Text.Json.Serialization;

// Rule 2.2 — shared API contract enum: consumed by DTOs in this (leaf) assembly AND by the
// Blazor client.
//
// Namespace note: this and everything else under Domain/ used to declare
// PoRepoLineTracker.Domain.Models — the namespace of a project that has not existed since the
// domain types were folded into .Shared. It was kept "so existing using-directives resolve
// unchanged", which only meant the misdirection outlived the reason for it.
namespace PoRepoLineTracker.Shared.Domain;

// The generic JsonStringEnumConverter<T>, not the open-ended JsonStringEnumConverter: the
// non-generic form constructs its per-enum converter reflectively at runtime, which defeats the
// source generator and keeps a reflection dependency alive through the trimmer (Rule 1.2).
[JsonConverter(typeof(JsonStringEnumConverter<ChartDisplayMode>))]
public enum ChartDisplayMode
{
    TrueData = 0,
    MovingAverage = 1
}
