using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using JobForge.Orchestrator.Scheduling;
using JobForge.Orchestrator.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class SchedulerLeaseTests
{
    private readonly ContainersFixture _infra;
    public SchedulerLeaseTests(ContainersFixture infra) => _infra = infra;

    [Fact]
    public async Task Second_lease_id_cannot_acquire_while_first_holds_it()
    {
        await using var redis = ConnectionMultiplexer.Connect(_infra.Redis.GetConnectionString());
        var a = new LeaseService(redis);
        var b = new LeaseService(redis);
        var ttl = TimeSpan.FromSeconds(2);
        var name = $"scheduler:lease:expiry:{Guid.NewGuid():N}";

        var got = await a.AcquireOrRenewAsync(name, "lease-a", ttl, CancellationToken.None);
        Assert.Equal("lease-a", got);

        var lost = await b.AcquireOrRenewAsync(name, "lease-b", ttl, CancellationToken.None);
        Assert.Null(lost);

        await Task.Delay(TimeSpan.FromMilliseconds(2500));
        var gotB = await b.AcquireOrRenewAsync(name, "lease-b", ttl, CancellationToken.None);
        Assert.Equal("lease-b", gotB);
    }

    [Fact]
    public async Task Non_holder_cannot_renew_foreign_lease()
    {
        await using var redis = ConnectionMultiplexer.Connect(_infra.Redis.GetConnectionString());
        var a = new LeaseService(redis);
        var b = new LeaseService(redis);
        var ttl = TimeSpan.FromSeconds(30);
        var name = $"scheduler:lease:renew:{Guid.NewGuid():N}";

        Assert.Equal("lease-a", await a.AcquireOrRenewAsync(name, "lease-a", ttl, CancellationToken.None));
        Assert.Equal("lease-a", await a.AcquireOrRenewAsync(name, "lease-a", ttl, CancellationToken.None));
        Assert.Null(await b.AcquireOrRenewAsync(name, "lease-b", ttl, CancellationToken.None));
    }

    [Fact]
    public async Task Tick_fires_cron_job_and_creates_outbox_once()
    {
        var fixedNow = new DateTimeOffset(2026, 10, 14, 12, 0, 15, TimeSpan.Zero);
        using var factory = new SharedWebFactory { Infra = _infra }
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                s.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedNow))));

        using var scope = factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<CronSchedulerService>();
        var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();

        var job = new JobDefinition
        {
            Id = Guid.NewGuid(),
            Name = $"every-minute-{Guid.NewGuid():N}",
            WebhookUrl = "http://example.local/h",
            Cron = "* * * * *",
            CreatedAt = DateTime.UtcNow
        };
        db.JobDefinitions.Add(job);
        await db.SaveChangesAsync();

        var fired = await scheduler.TickAsync(CancellationToken.None);
        Assert.True(fired >= 1);
        Assert.Single(await db.JobExecutions.Where(e => e.JobId == job.Id).ToListAsync());

        var outbox = await db.OutboxEvents.Where(o => o.ExecutionId ==
            db.JobExecutions.Where(e => e.JobId == job.Id).Select(e => e.Id).First())
            .ToListAsync();
        Assert.Single(outbox);

        var firedAgain = await scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(0, firedAgain);
        Assert.Single(await db.JobExecutions.Where(e => e.JobId == job.Id).ToListAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
