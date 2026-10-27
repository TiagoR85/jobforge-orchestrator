using System.Diagnostics.Metrics;
using JobForge.Orchestrator.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobForge.Orchestrator.Tests;

public class MetricsTests
{
    [Fact]
    public void Execution_finished_counts_the_status_and_records_the_duration()
    {
        using var metrics = CreateMetrics();
        using var listener = new MeterListener();
        var longs = CaptureLongs(listener);
        var doubles = CaptureDoubles(listener);
        listener.Start();

        metrics.ExecutionFinished("succeeded", 120);
        metrics.DeadLetter();

        Assert.Contains(("jobforge.executions.completed", 1L, "succeeded"), longs);
        Assert.Contains(("jobforge.dead_letters", 1L, (string?)null), longs);
        Assert.Contains(("jobforge.webhook.duration", 120d, "succeeded"), doubles);
    }

    [Fact]
    public void Missing_duration_is_counted_but_not_recorded()
    {
        using var metrics = CreateMetrics();
        using var listener = new MeterListener();
        var longs = CaptureLongs(listener);
        var doubles = CaptureDoubles(listener);
        listener.Start();

        metrics.ExecutionFinished("failed", 0);

        Assert.Contains(("jobforge.executions.completed", 1L, "failed"), longs);
        Assert.DoesNotContain(doubles, d => d.Name == "jobforge.webhook.duration");
    }

    [Fact]
    public void Exposes_the_four_business_instruments()
    {
        var published = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) => published.Add(instrument.Name)
        };
        listener.Start();

        using var metrics = CreateMetrics();

        Assert.Contains("jobforge.executions.completed", published);
        Assert.Contains("jobforge.executions.pending", published);
        Assert.Contains("jobforge.webhook.duration", published);
        Assert.Contains("jobforge.dead_letters", published);
    }

    private static JobForgeMetrics CreateMetrics() =>
        new(new UnusedScopeFactory(), NullLogger<JobForgeMetrics>.Instance);

    private static List<(string Name, long Value, string? Status)> CaptureLongs(MeterListener listener)
    {
        var values = new List<(string, long, string?)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobForgeMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            values.Add((instrument.Name, value, Tag(tags, "status"))));
        return values;
    }

    private static List<(string Name, double Value, string? Status)> CaptureDoubles(MeterListener listener)
    {
        var values = new List<(string, double, string?)>();
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            values.Add((instrument.Name, value, Tag(tags, "status"))));
        return values;
    }

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key) return tag.Value?.ToString();
        }
        return null;
    }

    private sealed class UnusedScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("the pending gauge is not observed here");
    }
}
