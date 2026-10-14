using System.Text.Json;

namespace JobForge.Orchestrator.Domain;

public static class OutboxFactory
{
    public static JobExecution CreateExecution(JobDefinition job, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        JobId = job.Id,
        Status = ExecutionStatus.Pending,
        ScheduledAt = now,
        TraceId = Guid.NewGuid().ToString("N")
    };

    public static OutboxEvent CreateOutbox(JobExecution execution, JobDefinition job, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ExecutionId = execution.Id,
        Payload = JsonSerializer.Serialize(new OutboxEnvelope(
            execution.Id, job.Id, job.WebhookUrl, job.Payload, job.Method,
            job.TimeoutSeconds, job.MaxRetries, Attempt: 0, execution.TraceId)),
        CreatedAt = now
    };
}
