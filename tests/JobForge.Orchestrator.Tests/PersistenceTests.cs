using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Persistence;
using JobForge.Orchestrator.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class PersistenceTests
{
    private readonly ContainersFixture _c;
    public PersistenceTests(ContainersFixture c) => _c = c;

    private static JobForgeDbContext NewDb(PostgreSqlContainer pg)
    {
        var options = new DbContextOptionsBuilder<JobForgeDbContext>()
            .UseNpgsql(pg.GetConnectionString())
            .UseOpenIddict()
            .Options;
        return new JobForgeDbContext(options);
    }

    [Fact]
    public async Task Job_definition_roundtrips_with_execution()
    {
        await using var db = NewDb(_c.Postgres);
        await db.Database.MigrateAsync();

        var job = new JobDefinition
        {
            Id = Guid.NewGuid(),
            Name = "daily-report",
            WebhookUrl = "http://example.local/hook",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        };
        job.Executions.Add(new JobExecution
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ScheduledAt = DateTime.UtcNow
        });
        db.JobDefinitions.Add(job);
        await db.SaveChangesAsync();

        await using var db2 = NewDb(_c.Postgres);
        var loaded = await db2.JobDefinitions.Include(j => j.Executions)
            .SingleAsync(j => j.Id == job.Id);
        Assert.Equal("daily-report", loaded.Name);
        Assert.Single(loaded.Executions);
        Assert.Equal(ExecutionStatus.Pending, loaded.Executions[0].Status);
    }
}
