namespace JobForge.Orchestrator.Domain;

public sealed class JobDefinition
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string WebhookUrl { get; set; }
    public string Payload { get; set; } = "{}";
    public string Method { get; set; } = "POST";
    public string? Cron { get; set; }
    public int MaxRetries { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<JobExecution> Executions { get; set; } = [];
}
