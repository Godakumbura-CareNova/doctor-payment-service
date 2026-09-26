using Confluent.Kafka;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

public class KafkaEventProducer : IKafkaEventProducer
{
    private readonly KafkaSettings _settings;

    public KafkaEventProducer(IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task PublishAsync(EventOutbox outboxEntry, CancellationToken cancellationToken)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        await producer.ProduceAsync(
            outboxEntry.Topic,
            new Message<string, string>
            {
                Key = outboxEntry.EventKey,
                Value = outboxEntry.PayloadJson
            },
            cancellationToken);
    }
}
