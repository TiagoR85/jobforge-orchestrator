using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace JobForge.Orchestrator.Tests.Fixtures;

public sealed class ContainersFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition("containers")]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture> { }
