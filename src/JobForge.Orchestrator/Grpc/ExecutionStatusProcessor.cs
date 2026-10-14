using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;
using ExecutionStates = JobForge.Orchestrator.Domain.ExecutionStatus;

namespace JobForge.Orchestrator.Grpc;

public sealed class ExecutionStatusProcessor(JobForgeDbContext db, TimeProvider clock)
{
    public async Task<bool> PersistAsync(StatusReport report, CancellationToken ct)
    {
        if (!Guid.TryParse(report.ExecutionId, out var id)) return false;
        var execution = await db.JobExecutions.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (execution is null) return false;

        execution.Status = report.Status;
        execution.Attempt = report.Attempt;
        execution.HttpStatusCode = report.HttpStatus == 0 ? null : report.HttpStatus;
        execution.Error = string.IsNullOrEmpty(report.Error) ? null : report.Error;
        if (report.Status == ExecutionStates.Running && execution.StartedAt is null)
            execution.StartedAt = clock.GetUtcNow().UtcDateTime;
        if (report.Status is ExecutionStates.Succeeded or ExecutionStates.Failed or ExecutionStates.Dead)
            execution.FinishedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
        return true;
    }
}
