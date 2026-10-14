using System.Text;
using Confluent.Kafka;
using JobForge.Orchestrator.Domain;

namespace JobForge.Orchestrator.Outbox;

public sealed class KafkaOutboxPublisher : IDisposable
{
    public const string Topic = "jobs.dispatch";

    private readonly IProducer<string, string> _producer;

    public KafkaOutboxPublisher(string bootstrapServers)
    {
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            ClientId = "jobforge-orchestrator",
            AllowAutoCreateTopics = true,
            EnableIdempotence = true
        }).Build();
    }

    public async Task PublishAsync(OutboxEvent evt, CancellationToken ct)
    {
        var message = new Message<string, string>
        {
            Key = evt.ExecutionId.ToString(),
            Value = evt.Payload,
            Headers = new Headers { { "x-attempt", Encoding.UTF8.GetBytes("0") } }
        };
        await _producer.ProduceAsync(Topic, message, ct);
    }

    public void Dispose() => _producer.Dispose();
}
