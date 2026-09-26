namespace DoctorPayment.Api.Config;

/// <summary>Kafka connection and outbox retry settings. Bound from "Kafka".</summary>
public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ConsumerGroupId { get; set; } = "carenova-payment-verification";

    /// <summary>How many times the outbox publisher tries an event before leaving it for a person to look at.</summary>
    public int PublishRetryLimit { get; set; } = 3;
}
