namespace JobForge.Orchestrator.Domain;

public sealed record OutboxEnvelope(
    Guid ExecutionId,
    Guid JobId,
    string WebhookUrl,
    string Payload,
    string Method,
    int TimeoutSeconds,
    int MaxRetries,
    int Attempt,
    string TraceId);
