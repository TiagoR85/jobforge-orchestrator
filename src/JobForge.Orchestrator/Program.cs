using JobForge.Orchestrator.Api;
using JobForge.Orchestrator.Configuration;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast: never start half-configured (same rule as the other families).
RequiredConfig.Ensure(builder.Configuration,
    "ConnectionStrings:Postgres", "Redis:ConnectionString",
    "Kafka:BootstrapServers", "OpenIddict:ClientSecret");

builder.Services.AddDbContext<JobForgeDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IdempotencyService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => "jobforge-orchestrator");
app.MapJobEndpoints();
app.MapExecutionEndpoints();
app.Run();

public partial class Program;
