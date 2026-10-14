using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace JobForge.Orchestrator.Auth;

public static class ScopePolicies
{
    public const string Write = "JobsWrite";
    public const string Read = "JobsRead";

    public static void AddJobForgePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Write,
            policy => policy.RequireAssertion(ctx => HasScope(ctx.User, "jobs:write")));
        options.AddPolicy(Read,
            policy => policy.RequireAssertion(ctx => HasScope(ctx.User, "jobs:read")));
    }

    public static bool HasScope(ClaimsPrincipal principal, string scope)
    {
        var scopes = principal.FindAll("scope")
            .Concat(principal.FindAll("scp"))
            .SelectMany(c => c.Value.Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return scopes.Contains(scope, StringComparer.Ordinal);
    }
}
