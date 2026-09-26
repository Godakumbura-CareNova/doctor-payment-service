using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IKafkaEventProducer
{
    Task PublishAsync(EventOutbox outboxEntry, CancellationToken cancellationToken);
}
