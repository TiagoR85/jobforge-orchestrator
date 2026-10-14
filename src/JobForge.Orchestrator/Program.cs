using System.Security.Claims;
using JobForge.Orchestrator.Api;
using JobForge.Orchestrator.Auth;
using JobForge.Orchestrator.Configuration;
using JobForge.Orchestrator.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();
    await db.Database.MigrateAsync();
    await OidcSeed.EnsureSeededAsync(app.Services,
        app.Configuration["OpenIddict:ClientSecret"]!);
}

app.MapGet("/", () => "jobforge-orchestrator");
app.MapJobEndpoints();
app.MapExecutionEndpoints();
app.Run();

public partial class Program;
