namespace DoctorPayment.Api.Models;

/// <summary>
/// A Kafka event waiting to be published. It is saved in the same database save as the change it describes, so an
/// event is never lost when Kafka is down; <see cref="Services.OutboxPublisherService"/> sends it later.
/// </summary>
public class EventOutbox
{
    public int Id { get; set; }

    public Guid EventId { get; set; } = Guid.NewGuid();

    public string Topic { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string EventKey { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAtUtc { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastError { get; set; }
}
