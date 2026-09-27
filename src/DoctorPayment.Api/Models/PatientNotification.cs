using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>An in-app message for a patient created from a Kafka event, e.g. their order is ready for collection.</summary>
public class PatientNotification
{
    public int Id { get; set; }

    /// <summary>The Kafka event this came from; unique, so a redelivered event is not notified twice.</summary>
    public Guid EventId { get; set; }

    public Guid PatientId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    public int OrderId { get; set; }

    /// <summary>The order status the notification is about.</summary>
    [MaxLength(30)]
    public string OrderStatus { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
