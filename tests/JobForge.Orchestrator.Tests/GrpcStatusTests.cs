using System.Diagnostics.Metrics;
using Grpc.Net.Client;
using JobForge.Orchestrator.Grpc;
using JobForge.Orchestrator.Persistence;
using JobForge.Orchestrator.Telemetry;
using JobForge.Orchestrator.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class GrpcStatusTests : IDisposable
{
    private readonly SharedWebFactory _factory;
    public GrpcStatusTests(ContainersFixture infra) => _factory = new SharedWebFactory { Infra = infra };

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Report_stream_persists_execution_status()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();

        var jobId = Guid.NewGuid();
        db.JobDefinitions.Add(new Domain.JobDefinition
        {
            Id = jobId,
            Name = "grpc-fixture",
            WebhookUrl = "http://example.local/hook",
            CreatedAt = DateTime.UtcNow
        });
        var executionId = Guid.NewGuid();
        db.JobExecutions.Add(new Domain.JobExecution
        {
            Id = executionId,
            JobId = jobId,
            Status = Domain.ExecutionStatus.Pending,
            ScheduledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress!, new GrpcChannelOptions
        {
            HttpHandler = _factory.Server.CreateHandler()
        });
        var client = new ExecutionStatus.ExecutionStatusClient(channel);

        using var call = client.Report();
        await call.RequestStream.WriteAsync(new StatusReport
        {
            ExecutionId = executionId.ToString(),
            Attempt = 1,
            Status = "running",
            HttpStatus = 500,
            Error = "boom",
            DurationMs = 12
        });
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        var ack = call.ResponseStream.Current;
        Assert.True(ack.Persisted);
        await call.RequestStream.CompleteAsync();

        var updated = await db.JobExecutions.AsNoTracking().SingleAsync(e => e.Id == executionId);
        Assert.Equal("running", updated.Status);
        Assert.NotNull(updated.StartedAt);
        Assert.Equal(500, updated.HttpStatusCode);
    }

    [Fact]
    public async Task Terminal_reports_feed_the_business_metrics()
    {
        using var listener = new MeterListener();
        var longs = new List<(string Name, long Value, string? Status)>();
        var doubles = new List<(string Name, double Value)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobForgeMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            longs.Add((instrument.Name, value, StatusTag(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            doubles.Add((instrument.Name, value)));
        listener.Start();
        // Create the instruments while the listener is active (the singleton is lazy).
        _ = _factory.Services.GetRequiredService<JobForgeMetrics>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();
        var jobId = Guid.NewGuid();
        db.JobDefinitions.Add(new Domain.JobDefinition
        {
            Id = jobId,
            Name = "metrics-fixture",
            WebhookUrl = "http://example.local/hook",
            CreatedAt = DateTime.UtcNow
        });
        var succeededId = Guid.NewGuid();
        var deadId = Guid.NewGuid();
        db.JobExecutions.AddRange(
            new Domain.JobExecution { Id = succeededId, JobId = jobId, Status = Domain.ExecutionStatus.Pending, ScheduledAt = DateTime.UtcNow },
            new Domain.JobExecution { Id = deadId, JobId = jobId, Status = Domain.ExecutionStatus.Pending, ScheduledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress!, new GrpcChannelOptions
        {
            HttpHandler = _factory.Server.CreateHandler()
        });
        var client = new ExecutionStatus.ExecutionStatusClient(channel);

        using (var call = client.Report())
        {
            await call.RequestStream.WriteAsync(new StatusReport
            {
                ExecutionId = succeededId.ToString(), Attempt = 1, Status = "succeeded", HttpStatus = 200, DurationMs = 120
            });
            Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
            await call.RequestStream.WriteAsync(new StatusReport
            {
                ExecutionId = deadId.ToString(), Attempt = 3, Status = "dead", HttpStatus = 500, Error = "exhausted", DurationMs = 90
            });
            Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
            await call.RequestStream.CompleteAsync();
        }

        Assert.Contains(("jobforge.executions.completed", 1L, "succeeded"), longs);
        Assert.Contains(("jobforge.executions.completed", 1L, "dead"), longs);
        Assert.Contains(("jobforge.dead_letters", 1L, (string?)null), longs);
        Assert.Contains(("jobforge.webhook.duration", 120d), doubles);
    }

    private static string? StatusTag(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "status") return tag.Value?.ToString();
        }
        return null;
    }

    [Fact]
    public async Task Report_unknown_execution_returns_not_persisted()
    {
        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress!, new GrpcChannelOptions
        {
            HttpHandler = _factory.Server.CreateHandler()
        });
        var client = new ExecutionStatus.ExecutionStatusClient(channel);

        using var call = client.Report();
        await call.RequestStream.WriteAsync(new StatusReport
        {
            ExecutionId = Guid.NewGuid().ToString(),
            Status = "failed",
            Error = "gone"
        });
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.False(call.ResponseStream.Current.Persisted);
        await call.RequestStream.CompleteAsync();
    }
}
