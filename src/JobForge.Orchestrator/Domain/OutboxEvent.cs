namespace JobForge.Orchestrator.Domain;

public sealed class OutboxEvent
{
    public const string PendingStatus = "pending";
    public const string PublishedStatus = "published";

    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public required string Payload { get; set; }
    public string Status { get; set; } = PendingStatus;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
}
