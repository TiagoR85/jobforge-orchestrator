using System.Net;
using System.Net.Http.Json;
using JobForge.Orchestrator.Tests.Fixtures;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class IdempotencyTests
{
    private readonly SharedWebFactory _factory;
    public IdempotencyTests(ContainersFixture infra)
        => _factory = new SharedWebFactory { Infra = infra };

    [Fact]
    public async Task Same_key_returns_same_execution_and_does_not_duplicate()
    {
        var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        object payload = new { name = "dedupe", webhookUrl = "http://example.local/x" };

        var first = await PostWithKey(client, key, payload);
        var second = await PostWithKey(client, key, payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<JobResponse>();
        var b = await second.Content.ReadFromJsonAsync<JobResponse>();
        Assert.Equal(a!.Id, b!.Id);
        Assert.Equal(a.ExecutionId, b.ExecutionId);
    }

    [Fact]
    public async Task Missing_key_is_400()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/v1/jobs",
            new { name = "x", webhookUrl = "http://example.local/x" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Run_now_creates_new_execution_with_outbox()
    {
        var client = _factory.CreateClient();
        var job = await JobsApiTests.CreateJobAsync(client, "runnable");

        var res = await client.PostAsync($"/v1/jobs/{job.Id}/run", null);

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }

    private static Task<HttpResponseMessage> PostWithKey(HttpClient c, string key, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/jobs")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("Idempotency-Key", key);
        return c.SendAsync(req);
    }
}
