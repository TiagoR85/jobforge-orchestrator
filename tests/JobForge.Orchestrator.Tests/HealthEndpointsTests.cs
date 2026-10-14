using System.Net;
using JobForge.Orchestrator.Tests.Fixtures;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class HealthEndpointsTests : IDisposable
{
    private readonly SharedWebFactory _factory;
    public HealthEndpointsTests(ContainersFixture infra) => _factory = new SharedWebFactory { Infra = infra };

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Live_is_200_and_ready_is_200_with_real_dependencies()
    {
        var client = _factory.CreateClient();
        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.True(ready.IsSuccessStatusCode,
            $"ready={(int)ready.StatusCode}: {await ready.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Metrics_endpoint_exposes_prometheus_text()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("process_", body);
    }
}
