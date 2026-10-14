using Cronos;
using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobForge.Orchestrator.Scheduling;

public sealed class SchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public int TickSeconds { get; set; } = 30;
    public string LeaseName { get; set; } = "scheduler:lease";
}

public sealed class CronSchedulerService(
    IServiceScopeFactory scopes,
    LeaseService lease,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    ILogger<CronSchedulerService> logger) : BackgroundService
{
    private readonly string _leaseId = Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Scheduler disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.TickSeconds), clock, stoppingToken);
        }
    }

    /// <summary>Devolve o número de jobs cron disparados nesta janela.</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(60, options.Value.TickSeconds * 2));
        var holder = await lease.AcquireOrRenewAsync(options.Value.LeaseName, _leaseId, ttl, ct);
        if (holder is null) return 0; // outra réplica é o scheduler ativo

        var now = clock.GetUtcNow().UtcDateTime;
        var windowStart = now.AddSeconds(-options.Value.TickSeconds);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();

        var cronJobs = await db.JobDefinitions.AsNoTracking()
            .Where(j => j.Enabled && j.Cron != null)
            .ToListAsync(ct);

        var fired = 0;
        foreach (var job in cronJobs)
        {
            var expression = CronExpression.Parse(job.Cron!);
            var occurrence = expression.GetNextOccurrence(windowStart, TimeZoneInfo.Utc);
            if (occurrence is null || occurrence > now) continue;
            var occurrenceAt = DateTime.SpecifyKind(occurrence.Value, DateTimeKind.Utc);

            // Restart safety: nunca re-fire no mesmo slot cron (window cover por tick/janela).
            if (await db.JobExecutions.AsNoTracking()
                .AnyAsync(e => e.JobId == job.Id && e.ScheduledAt == occurrenceAt, ct))
                continue;

            await using var dbScope = scopes.CreateAsyncScope();
            var db2 = dbScope.ServiceProvider.GetRequiredService<JobForgeDbContext>();
            await using var tx = await db2.Database.BeginTransactionAsync(ct);

            var jobEntity = await db2.JobDefinitions.SingleAsync(j => j.Id == job.Id, ct);
            var execution = OutboxFactory.CreateExecution(jobEntity, occurrenceAt);
            db2.JobExecutions.Add(execution);
            db2.OutboxEvents.Add(OutboxFactory.CreateOutbox(execution, jobEntity, occurrenceAt));
            await db2.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            fired++;
        }
        return fired;
    }
}
