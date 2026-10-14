namespace JobForge.Orchestrator.Domain;

public sealed class JobExecution
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobDefinition? Job { get; set; }
    public string Status { get; set; } = ExecutionStatus.Pending;
    public int Attempt { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? Error { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
}
