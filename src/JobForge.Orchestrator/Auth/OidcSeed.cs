using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace JobForge.Orchestrator.Auth;

public static class OidcSeed
{
    public static async Task EnsureSeededAsync(IServiceProvider services, string clientSecret, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var name in new[] { "jobs:read", "jobs:write" })
        {
            if (await scopeManager.FindByNameAsync(name, ct) is null)
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = name,
                    DisplayName = name
                }, ct);
        }

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await EnsureClientAsync(manager, "jobforge-demo", "jobforge demo client", clientSecret, ct);
        await EnsureClientAsync(manager, "jobforge-console", "jobforge web console", clientSecret, ct);
    }

    private static async Task EnsureClientAsync(
        IOpenIddictApplicationManager manager, string clientId, string displayName, string secret, CancellationToken ct)
    {
        if (await manager.FindByClientIdAsync(clientId, ct) is not null) return;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            DisplayName = displayName,
            ClientType = ClientTypes.Confidential,
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials
            }
        };
        descriptor.Permissions.Add(Permissions.Prefixes.Scope + "jobs:read");
        descriptor.Permissions.Add(Permissions.Prefixes.Scope + "jobs:write");

        await manager.CreateAsync(descriptor, ct);
    }
}
