using Cronos;
using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobForge.Orchestrator.Api;

public sealed class IdempotencyService(JobForgeDbContext db, TimeProvider clock)
{
    public async Task<(JobDefinition Job, JobExecution? Execution, bool Replayed)>
        CreateJobAsync(string key, CreateJobRequest request, CancellationToken ct)
    {
        var existing = await db.IdempotencyKeys
            .FirstOrDefaultAsync(k => k.Key == key, ct);
        if (existing is not null)
        {
            var job = await db.JobDefinitions.AsNoTracking()
                .SingleAsync(j => j.Id == existing.JobId, ct);
            var execution = existing.ExecutionId is { } eid
                ? await db.JobExecutions.AsNoTracking().SingleAsync(e => e.Id == eid, ct)
                : null;
            return (job, execution, true);
        }

        Validate(request);
        if (!string.IsNullOrWhiteSpace(request.Cron)) CronExpression.Parse(request.Cron);

        var now = clock.GetUtcNow().UtcDateTime;
        var job2 = new JobDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            WebhookUrl = request.WebhookUrl.Trim(),
            Payload = request.Payload ?? "{}",
            Method = (request.Method?.Trim().ToUpperInvariant()) is { Length: > 0 } m ? m : "POST",
            Cron = string.IsNullOrWhiteSpace(request.Cron) ? null : request.Cron.Trim(),
            MaxRetries = request.MaxRetries ?? 5,
            TimeoutSeconds = request.TimeoutSeconds ?? 30,
            CreatedAt = now
        };
        db.JobDefinitions.Add(job2);

        JobExecution? execution2 = null;
        if (job2.Cron is null)
        {
            execution2 = OutboxFactory.CreateExecution(job2, now);
            job2.Executions.Add(execution2);
            db.OutboxEvents.Add(OutboxFactory.CreateOutbox(execution2, job2, now));
        }

        db.IdempotencyKeys.Add(new IdempotencyKey
        {
            Key = key,
            JobId = job2.Id,
            ExecutionId = execution2?.Id,
            CreatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return await CreateJobAsync(key, request, ct);
        }

        return (job2, execution2, false);
    }

    public async Task<JobExecution> RequestRunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.JobDefinitions.SingleAsync(j => j.Id == jobId && j.Enabled, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var execution = OutboxFactory.CreateExecution(job, now);
        db.JobExecutions.Add(execution);
        db.OutboxEvents.Add(OutboxFactory.CreateOutbox(execution, job, now));
        await db.SaveChangesAsync(ct);
        return execution;
    }

    private static void Validate(CreateJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("name is required");
        if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("webhookUrl must be an absolute http(s) url");
        var method = request.Method?.Trim().ToUpperInvariant();
        if (method is not null && method is not ("GET" or "POST"))
            throw new ArgumentException("method must be GET or POST");
    }
}
