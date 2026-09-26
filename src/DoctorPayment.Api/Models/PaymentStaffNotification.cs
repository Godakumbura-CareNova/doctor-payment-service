using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>A message for pharmacy staff created from a Kafka payment event, e.g. a bank receipt waiting for verification.</summary>
public class PaymentStaffNotification
{
    public int Id { get; set; }

    /// <summary>The Kafka event this came from; unique, so a redelivered event is not notified twice.</summary>
    public Guid EventId { get; set; }

    [MaxLength(50)]
    public string NotificationType { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public int PaymentId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
