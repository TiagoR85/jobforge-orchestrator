namespace JobForge.Orchestrator.Domain;

public static class ExecutionStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Dead = "dead";
}
