using System.Text;
using Confluent.Kafka;
using JobForge.Orchestrator.Domain;
using JobForge.Orchestrator.Outbox;
using JobForge.Orchestrator.Persistence;
using JobForge.Orchestrator.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobForge.Orchestrator.Tests;

[Collection("containers")]
public class OutboxDispatcherTests
{
    private readonly ContainersFixture _infra;
    public OutboxDispatcherTests(ContainersFixture infra) => _infra = infra;

    [Fact]
    public async Task Dispatcher_publishes_pending_event_and_marks_published()
    {
        using var factory = new SharedWebFactory { Infra = _infra };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobForgeDbContext>();

        var executionId = Guid.NewGuid();
        var evt = new OutboxEvent
        {
            Id = Guid.NewGuid(),
            ExecutionId = executionId,
            Payload = $$"""{"executionId":"{{executionId}}"}""",
            CreatedAt = DateTime.UtcNow
        };
        db.OutboxEvents.Add(evt);
        await db.SaveChangesAsync();

        var publisher = new KafkaOutboxPublisher(_infra.Kafka.GetBootstrapAddress());
        var dispatcher = new OutboxDispatcher(scope.ServiceProvider, publisher,
            NullLogger<OutboxDispatcher>.Instance,
            Options.Create(new OutboxOptions { BatchSize = 1000 }));

        var published = await dispatcher.RunOnceAsync(CancellationToken.None);
        Assert.True(published >= 1);

        await db.Entry(evt).ReloadAsync();
        Assert.Equal(OutboxEvent.PublishedStatus, evt.Status);

        var seen = new List<string>();
        var found = await ConsumeUntilAsync(_infra.Kafka.GetBootstrapAddress(),
            evt.ExecutionId.ToString(), TimeSpan.FromSeconds(30), seen);
        Assert.True(found, $"mensagem não encontrada; vistos={seen.Count}: " +
            string.Join(" | ", seen.Select(v => v.Length > 120 ? v[..120] : v)));
    }

    private static async Task<bool> ConsumeUntilAsync(string bootstrap, string needle, TimeSpan timeout, List<string> seen)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "test-" + Guid.NewGuid(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe("jobs.dispatch");
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (consumer.Consume(TimeSpan.FromSeconds(1)) is { } cr)
                {
                    seen.Add(cr.Message.Value ?? "<null>");
                    if (cr.Message.Value?.Contains(needle, StringComparison.Ordinal) == true)
                        return true;
                }
            }
            return false;
        }
        finally
        {
            consumer.Close();
        }
    }
}
