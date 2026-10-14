using JobForge.Orchestrator.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Orchestrator.Persistence;

public sealed class JobForgeDbContext(DbContextOptions<JobForgeDbContext> options) : DbContext(options)
{
    public DbSet<JobDefinition> JobDefinitions => Set<JobDefinition>();
    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobDefinition>(e =>
        {
            e.ToTable("job_definitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.WebhookUrl).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.Cron).HasMaxLength(100);
            e.HasMany(x => x.Executions).WithOne(x => x.Job!)
                .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Enabled);
        });

        modelBuilder.Entity<JobExecution>(e =>
        {
            e.ToTable("job_executions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Error).HasMaxLength(4000);
            e.Property(x => x.TraceId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.Status, x.ScheduledAt });
            e.HasIndex(x => x.JobId);
        });

        modelBuilder.Entity<OutboxEvent>(e =>
        {
            e.ToTable("outbox_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<IdempotencyKey>(e =>
        {
            e.ToTable("idempotency_keys");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}
