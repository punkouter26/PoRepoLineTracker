using System.Text.Json.Serialization;

namespace PoRepoLineTracker.Domain.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChartDisplayMode
{
    TrueData = 0,
    MovingAverage = 1
}