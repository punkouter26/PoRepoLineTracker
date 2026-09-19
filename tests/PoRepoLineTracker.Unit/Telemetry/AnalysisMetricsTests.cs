using System.Diagnostics.Metrics;
using FluentAssertions;
using PoRepoLineTracker.API.Telemetry;
using PoRepoLineTracker.Shared.Domain;

namespace PoRepoLineTracker.Unit.Telemetry;

/// <summary>
/// Pins that <see cref="AnalysisMetrics"/> records into the same <see cref="System.Diagnostics.Metrics.Meter"/>
/// the OpenTelemetry pipeline subscribes to via <c>TelemetryServiceExtensions.AddTelemetry</c>.
///
/// <para>If these were on a fresh <c>Meter</c>, an OTel pipeline built only against
/// <c>AppTelemetry.SourceName</c> would silently drop the samples — and the live view would
/// stop showing "lines counted" without any test failing. This test is the guard against that.</para>
/// </summary>
public class AnalysisMetricsTests
{
    [Fact]
    public void LinesCounted_records_into_the_AppTelemetry_meter_and_a_listener_sees_the_sample()
    {
        var listener = new MeterListener();
        var seen = new List<(long Value, IReadOnlyDictionary<string, object?> Tags)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AppTelemetry.SourceName &&
                instrument.Name == AnalysisMetrics.LinesCountedName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        // MeasurementCallback<long> in net10.0 is (Instrument, long, ReadOnlySpan<KeyValuePair<string, object?>>, object?).
        // The compiler resolves the lambda to the 4-arg overload.
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            seen.Add((value, ToReadOnly(tags))));
        listener.Start();

        var repoId = new RepositoryId(Guid.NewGuid());
        AnalysisMetrics.RecordLinesCounted(repoId, 4_242);

        listener.RecordObservableInstruments();
        listener.Dispose();

        seen.Should().ContainSingle();
        seen[0].Value.Should().Be(4_242L);
        seen[0].Tags.Should().ContainKey("repo")
            .WhoseValue.Should().Be(repoId.Value.ToString());
    }

    [Fact]
    public void CommitCountDuration_records_into_the_AppTelemetry_meter_and_a_listener_sees_the_sample()
    {
        var listener = new MeterListener();
        var seen = new List<(double Value, IReadOnlyDictionary<string, object?> Tags)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AppTelemetry.SourceName &&
                instrument.Name == AnalysisMetrics.CommitCountDurationName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            seen.Add((value, ToReadOnly(tags))));
        listener.Start();

        var repoId = new RepositoryId(Guid.NewGuid());
        AnalysisMetrics.RecordCommitCountDuration(repoId, 12.5);

        listener.RecordObservableInstruments();
        listener.Dispose();

        seen.Should().ContainSingle();
        seen[0].Value.Should().Be(12.5);
        seen[0].Tags["repo"].Should().Be(repoId.Value.ToString());
    }

    private static IReadOnlyDictionary<string, object?> ToReadOnly(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kv in tags) dict[kv.Key] = kv.Value;
        return dict;
    }
}
