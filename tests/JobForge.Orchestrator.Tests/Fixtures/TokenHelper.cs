using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JobForge.Orchestrator.Tests.Fixtures;
using Xunit;

namespace JobForge.Orchestrator.Tests.Fixtures;

public static class TokenHelper
{
    public static async Task<string> RequestTokenAsync(HttpClient client, string scope)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "jobforge-demo",
            ["client_secret"] = "test-secret",
            ["scope"] = scope
        };
        var res = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    public static async Task<HttpClient> CreateAuthorizedClientAsync(SharedWebFactory factory, string scope)
    {
        var client = factory.CreateClient();
        var token = await RequestTokenAsync(client, scope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
