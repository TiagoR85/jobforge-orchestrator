using JobForge.Orchestrator.Auth;
using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Orchestrator.Api;

public static class JobsEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/jobs");

        group.MapPost("/", async (
            HttpContext http, CreateJobRequest request, IdempotencyService svc, CancellationToken ct) =>
        {
            if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var raw)
                || string.IsNullOrWhiteSpace(raw))
                return Results.BadRequest(new { error = "Idempotency-Key header is required" });

            try
            {
                var (job, execution, replayed) = await svc.CreateJobAsync(raw.ToString(), request, ct);
                var response = ToJobResponse(job, execution);
                return replayed
                    ? Results.Ok(response)
                    : Results.Created($"/v1/jobs/{job.Id}", response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization(ScopePolicies.Write);

        group.MapPost("/{id:guid}/run", async (Guid id, IdempotencyService svc, CancellationToken ct) =>
        {
            try
            {
                var execution = await svc.RequestRunAsync(id, ct);
                return Results.Accepted($"/v1/executions/{execution.Id}",
                    new ExecutionResponse(execution.Id, execution.JobId, execution.Status,
                        execution.Attempt, execution.HttpStatusCode, execution.Error,
                        execution.ScheduledAt, execution.StartedAt, execution.FinishedAt, execution.TraceId));
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        }).RequireAuthorization(ScopePolicies.Write);

        group.MapGet("/", async (JobForgeDbContext db, CancellationToken ct) =>
        {
            var jobs = await db.JobDefinitions.AsNoTracking()
                .OrderBy(j => j.CreatedAt).ToListAsync(ct);
            var result = new List<JobResponse>();
            foreach (var job in jobs)
            {
                var last = await db.JobExecutions.AsNoTracking()
                    .Where(e => e.JobId == job.Id)
                    .OrderByDescending(e => e.ScheduledAt)
                    .FirstOrDefaultAsync(ct);
                result.Add(ToJobResponse(job, last));
            }
            return Results.Ok(result);
        }).RequireAuthorization(ScopePolicies.Read);

        group.MapGet("/{id:guid}", async (Guid id, JobForgeDbContext db, CancellationToken ct) =>
        {
            var job = await db.JobDefinitions.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null) return Results.NotFound();
            var last = await db.JobExecutions.AsNoTracking()
                .Where(e => e.JobId == id)
                .OrderByDescending(e => e.ScheduledAt)
                .FirstOrDefaultAsync(ct);
            return Results.Ok(ToJobResponse(job, last));
        }).RequireAuthorization(ScopePolicies.Read);

        group.MapDelete("/{id:guid}", async (Guid id, JobForgeDbContext db, CancellationToken ct) =>
        {
            var job = await db.JobDefinitions.FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null) return Results.NotFound();
            job.Enabled = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(ScopePolicies.Write);

        return app;
    }

    internal static JobResponse ToJobResponse(JobDefinition job, JobExecution? last) => new(
        job.Id, job.Name, job.WebhookUrl, job.Payload, job.Method, job.Cron,
        job.MaxRetries, job.TimeoutSeconds, job.Enabled, last?.Id, last?.Status);
}
