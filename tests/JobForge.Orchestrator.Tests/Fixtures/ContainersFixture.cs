using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace JobForge.Orchestrator.Tests.Fixtures;

public sealed class ContainersFixture : IAsyncLifetime
{
    public static readonly string[] Topics = ["jobs.dispatch", "jobs.dead"];

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();
    public KafkaContainer Kafka { get; } = new KafkaBuilder("confluentinc/cp-kafka:7.9.0").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync(), Kafka.StartAsync());
        await CreateTopicsAsync();
    }

    private async Task CreateTopicsAsync()
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = Kafka.GetBootstrapAddress() }).Build();
        var specs = Topics.Select(t => new TopicSpecification
        {
            Name = t,
            NumPartitions = 3,
            ReplicationFactor = 1
        }).ToArray();
        try
        {
            await admin.CreateTopicsAsync(specs);
        }
        catch (CreateTopicsException ex) when (
            ex.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
            // corrida entre workers: já criado é ok
        }
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            Postgres.DisposeAsync().AsTask(),
            Redis.DisposeAsync().AsTask(),
            Kafka.DisposeAsync().AsTask());
    }
}

[CollectionDefinition("containers")]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture> { }
