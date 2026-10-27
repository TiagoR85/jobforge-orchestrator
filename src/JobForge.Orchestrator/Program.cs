using System.Security.Claims;
using JobForge.Orchestrator.Api;
using JobForge.Orchestrator.Auth;
using JobForge.Orchestrator.Configuration;
using JobForge.Orchestrator.Grpc;
using JobForge.Orchestrator.Health;
using JobForge.Orchestrator.Outbox;
using JobForge.Orchestrator.Persistence;
using JobForge.Orchestrator.Scheduling;
using JobForge.Orchestrator.Telemetry;
using Microsoft.EntityFrameworkCore;
using NLog.Web;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Metrics;
using StackExchange.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Host.UseNLog();

// Fail-fast: never start half-configured (same rule as the other families).
RequiredConfig.Ensure(builder.Configuration,
    "ConnectionStrings:Postgres", "Redis:ConnectionString",
    "Kafka:BootstrapServers", "OpenIddict:ClientSecret");

builder.Services.AddDbContext<JobForgeDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
    options.UseOpenIddict();
});

builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<JobForgeDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowClientCredentialsFlow();
        options.RegisterScopes("jobs:read", "jobs:write");
        options.AddDevelopmentEncryptionCertificate();
        options.AddDevelopmentSigningCertificate();
        options.DisableAccessTokenEncryption(); // demo over plain HTTP; production keeps encryption
        options.SetIssuer(builder.Configuration["Jwt:Authority"]!);
        options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(descriptor =>
            descriptor.UseInlineHandler(context =>
            {
                if (context.Request.IsClientCredentialsGrantType())
                {
                    var identity = new ClaimsIdentity(
                        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        Claims.Name,
                        Claims.Role);
                    identity.SetClaim(Claims.Subject, context.Request.ClientId!);
                    var principal = new ClaimsPrincipal(identity);
                    principal.SetScopes(context.Request.GetScopes());
                    context.Principal = principal;
                }
                return ValueTask.CompletedTask;
            }));
        options.UseAspNetCore().DisableTransportSecurityRequirement();
    })
    // Same-process issuer + resource: validate locally (no HTTP metadata round-trip,
    // works at any test port). Cross-service scenarios would use JwtBearer + Authority.
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddAuthorization(options => options.AddJobForgePolicies());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddGrpc();
builder.Services.AddScoped<ExecutionStatusProcessor>();
builder.Services.AddSingleton<JobForgeMetrics>();

builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));
builder.Services.AddSingleton<LeaseService>();
// Singleton própria + hosted via factory: o teste resolve CronSchedulerService direto.
builder.Services.AddSingleton<CronSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CronSchedulerService>());

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddSingleton(_ => new KafkaOutboxPublisher(
    builder.Configuration["Kafka:BootstrapServers"]!));
builder.Services.AddHostedService<OutboxDispatcher>();

builder.Services.AddHealthChecks()
    .AddCheck("self", static () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!, tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddCheck<KafkaHealthCheck>("kafka", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(JobForgeMetrics.MeterName)
        .AddPrometheusExporter());

var app = builder.Build();

// Create the meter at startup: the singleton is otherwise lazy and /metrics would miss the
// observable gauge until the first runner report.
app.Services.GetRequiredService<JobForgeMetrics>();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();
    await db.Database.MigrateAsync();
    await OidcSeed.EnsureSeededAsync(app.Services,
        app.Configuration["OpenIddict:ClientSecret"]!);
}

app.MapGet("/", () => "jobforge-orchestrator");
app.MapHealthChecks("/health/live",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = static r => r.Tags.Contains("live")
    });
app.MapHealthChecks("/health/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = static r => r.Tags.Contains("ready"),
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    e => e.Key,
                    e => new { status = e.Value.Status.ToString(), error = e.Value.Description })
            }));
        }
    });
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");
app.MapGrpcService<ExecutionStatusServiceImpl>();
app.MapJobEndpoints();
app.MapExecutionEndpoints();
app.Run();

public partial class Program;
