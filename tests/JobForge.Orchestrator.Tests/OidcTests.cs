using System.Net;
using System.Net.Http.Headers;
using JobForge.Orchestrator.Tests.Fixtures;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class OidcTests
{
    private readonly SharedWebFactory _factory;
    public OidcTests(ContainersFixture infra)
        => _factory = new SharedWebFactory { Infra = infra };

    [Fact]
    public async Task Client_credentials_flow_yields_token_that_reads_jobs()
    {
        var client = _factory.CreateClient();
        var token = await TokenHelper.RequestTokenAsync(client, "jobs:read");
        Assert.False(string.IsNullOrWhiteSpace(token));

        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/jobs");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_get_jobs_is_401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/v1/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Write_scope_cannot_create_jobs()
    {
        var client = _factory.CreateClient();
        var token = await TokenHelper.RequestTokenAsync(client, "jobs:read");
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/jobs")
        {
            Content = new StringContent(
                """{"name":"x","webhookUrl":"http://example.local/x"}""",
                System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
