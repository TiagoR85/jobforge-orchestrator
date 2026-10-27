using System.Diagnostics.Metrics;
using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Orchestrator.Telemetry;

/// <summary>Business instruments exported on /metrics; the meter is enabled in Program.cs.</summary>
public sealed class JobForgeMetrics : IDisposable
{
    public const string MeterName = "JobForge.Orchestrator";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _executionsCompleted;
    private readonly Counter<long> _deadLetters;
    private readonly Histogram<double> _webhookDuration;
    private readonly ILogger<JobForgeMetrics> _logger;

    public JobForgeMetrics(IServiceScopeFactory scopes, ILogger<JobForgeMetrics> logger)
    {
        _logger = logger;
        _executionsCompleted = _meter.CreateCounter<long>(
            "jobforge.executions.completed", "{execution}", "Executions reaching a terminal status");
        _deadLetters = _meter.CreateCounter<long>(
            "jobforge.dead_letters", "{execution}", "Executions that exhausted their retries");
        _webhookDuration = _meter.CreateHistogram<double>(
            "jobforge.webhook.duration", "ms", "Webhook duration reported by the runner");
        _meter.CreateObservableGauge(
            "jobforge.executions.pending",
            () => Pending(scopes),
            "{execution}",
            "Executions waiting to be dispatched");
    }

    /// <summary>Counts one execution reaching a terminal status and its webhook duration, when reported.</summary>
    public void ExecutionFinished(string status, long durationMs)
    {
        _executionsCompleted.Add(1, new KeyValuePair<string, object?>("status", status));
        if (durationMs > 0)
            _webhookDuration.Record(durationMs, new KeyValuePair<string, object?>("status", status));
    }

    public void DeadLetter() => _deadLetters.Add(1);

    public void Dispose() => _meter.Dispose();

    // Observable callbacks are synchronous; one COUNT per scrape (15s) is cheap.
    private IEnumerable<Measurement<long>> Pending(IServiceScopeFactory scopes)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();
            var count = db.JobExecutions.Count(e => e.Status == ExecutionStatus.Pending);
            return [new Measurement<long>(count)];
        }
        catch (Exception ex)
        {
            // A failing scrape must not break the whole /metrics response.
            _logger.LogWarning(ex, "Pending executions gauge failed");
            return [];
        }
    }
}
