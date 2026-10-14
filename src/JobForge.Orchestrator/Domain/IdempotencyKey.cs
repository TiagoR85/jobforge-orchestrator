namespace JobForge.Orchestrator.Domain;

public sealed class IdempotencyKey
{
    public required string Key { get; set; }
    public Guid JobId { get; set; }
    public Guid? ExecutionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
