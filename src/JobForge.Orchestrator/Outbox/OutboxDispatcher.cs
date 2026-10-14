using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobForge.Orchestrator.Outbox;

public sealed class OutboxOptions
{
    public bool Enabled { get; set; } = true;
    public int TickMilliseconds { get; set; } = 500;
    public int BatchSize { get; set; } = 20;
}

public sealed class OutboxDispatcher(
    IServiceProvider services,
    KafkaOutboxPublisher publisher,
    ILogger<OutboxDispatcher> logger,
    IOptions<OutboxOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Outbox dispatcher disabled");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch failed");
            }
            await Task.Delay(options.Value.TickMilliseconds, stoppingToken);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();

        var batch = await db.OutboxEvents.AsNoTracking()
            .Where(e => e.Status == OutboxEvent.PendingStatus)
            .OrderBy(e => e.CreatedAt)
            .Take(options.Value.BatchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var evt in batch)
        {
            await publisher.PublishAsync(evt, ct); // ack do Kafka antes de marcar
            var tracked = await db.OutboxEvents.SingleAsync(e => e.Id == evt.Id, ct);
            tracked.Status = OutboxEvent.PublishedStatus;
            await db.SaveChangesAsync(ct);
            published++;
        }
        return published;
    }
}
