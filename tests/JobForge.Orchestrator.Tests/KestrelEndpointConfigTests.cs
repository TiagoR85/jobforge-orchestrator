using System.Text.Json;
using Xunit;

namespace JobForge.Orchestrator.Tests;

public class KestrelEndpointConfigTests
{
    [Fact]
    public void Http_port_is_http1_and_grpc_port_is_http2_only()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "..", "src", "JobForge.Orchestrator", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var endpoints = doc.RootElement.GetProperty("Kestrel").GetProperty("Endpoints");

        Assert.Equal("Http1",
            endpoints.GetProperty("http").GetProperty("Protocols").GetString());
        Assert.Equal("http://localhost:8094",
            endpoints.GetProperty("http").GetProperty("Url").GetString());
        Assert.Equal("Http2",
            endpoints.GetProperty("grpc").GetProperty("Protocols").GetString());
        Assert.Equal("http://localhost:8097",
            endpoints.GetProperty("grpc").GetProperty("Url").GetString());
    }
}
