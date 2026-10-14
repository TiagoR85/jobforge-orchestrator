using System.Net;
using System.Net.Http.Json;
using JobForge.Orchestrator.Tests.Fixtures;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class JobsApiTests
{
    private readonly SharedWebFactory _factory;
    public JobsApiTests(ContainersFixture infra)
        => _factory = new SharedWebFactory { Infra = infra };

    [Fact]
    public async Task Post_job_with_key_creates_definition_and_immediate_execution()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/jobs")
        {
            Content = JsonContent.Create(new
            {
                name = "ping",
                webhookUrl = "http://example.local/hook",
                payload = "{\"hello\":\"world\"}",
                method = "POST"
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JobResponse>();
        Assert.NotNull(body);
        Assert.NotNull(body!.ExecutionId);
        Assert.Equal("pending", body.ExecutionStatus);
    }

    [Fact]
    public async Task Invalid_webhook_url_is_400()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/jobs")
        {
            Content = JsonContent.Create(new { name = "bad", webhookUrl = "not-a-url" })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Delete_disables_job()
    {
        var client = _factory.CreateClient();
        var created = await CreateJobAsync(client, "to-disable");
        var res = await client.DeleteAsync($"/v1/jobs/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var list = await client.GetFromJsonAsync<List<JobResponse>>("/v1/jobs");
        Assert.False(list!.Single(j => j.Id == created.Id).Enabled);
    }

    internal static async Task<JobResponse> CreateJobAsync(HttpClient client, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/jobs")
        {
            Content = JsonContent.Create(new { name, webhookUrl = "http://example.local/hook" })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JobResponse>())!;
    }
}

public sealed record JobResponse(
    Guid Id, string Name, string WebhookUrl, string? Payload, string Method,
    string? Cron, int MaxRetries, int TimeoutSeconds, bool Enabled,
    Guid? ExecutionId, string? ExecutionStatus);
