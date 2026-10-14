using Grpc.Net.Client;
using JobForge.Orchestrator.Grpc;
using JobForge.Orchestrator.Persistence;
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
