using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Orchestrator.Api;

public static class ExecutionsEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/executions");

        group.MapGet("/", async (
            JobForgeDbContext db, string? status, int page = 1, int size = 20,
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            size = Math.Clamp(size, 1, 100);
            var query = db.JobExecutions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(e => e.Status == status);
            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(e => e.ScheduledAt)
                .Skip((page - 1) * size).Take(size)
                .Select(e => new ExecutionResponse(e.Id, e.JobId, e.Status, e.Attempt,
                    e.HttpStatusCode, e.Error, e.ScheduledAt, e.StartedAt, e.FinishedAt, e.TraceId))
                .ToListAsync(ct);
            return Results.Ok(new ExecutionPage(page, size, total, items));
        });

        group.MapGet("/{id:guid}", async (Guid id, JobForgeDbContext db, CancellationToken ct) =>
        {
            var e = await db.JobExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            return Results.Ok(new ExecutionResponse(e.Id, e.JobId, e.Status, e.Attempt,
                e.HttpStatusCode, e.Error, e.ScheduledAt, e.StartedAt, e.FinishedAt, e.TraceId));
        });

        return app;
    }
}
