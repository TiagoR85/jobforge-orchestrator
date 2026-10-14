using JobForge.Orchestrator.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace JobForge.Orchestrator.Tests.Fixtures;

public sealed class SharedWebFactory : WebApplicationFactory<Program>
{
    public required ContainersFixture Infra { get; init; }
    public string SchedulerLeaseName { get; init; } = $"scheduler:lease:test:{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
        [
            new("ConnectionStrings:Postgres", Infra.Postgres.GetConnectionString()),
            new("Redis:ConnectionString", Infra.Redis.GetConnectionString()),
            new("Kafka:BootstrapServers", "localhost:9092"),
            new("OpenIddict:ClientSecret", "test-secret"),
            new("Scheduler:Enabled", "false"),
            new("Scheduler:LeaseName", SchedulerLeaseName),
            new("Outbox:Enabled", "false"),
            new("Kestrel:Endpoints:http:Url", "http://127.0.0.1:0"),
            new("Kestrel:Endpoints:grpc:Url", "http://127.0.0.1:0"),
        ]));
    }
}
