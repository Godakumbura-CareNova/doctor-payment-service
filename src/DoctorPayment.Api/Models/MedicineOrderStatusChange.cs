using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>The pharmacy order statuses, as the pharmacy-order service names them.</summary>
public static class MedicineOrderStatuses
{
    public const string Pending = "Pending";
    public const string ReadyForCollection = "ReadyForCollection";
    public const string Collected = "Collected";
    public const string Rejected = "Rejected";
}

public enum PatientNotificationStatus
{
    /// <summary>The Kafka event has not been processed yet.</summary>
    Pending,

    /// <summary>The patient's notification was created.</summary>
    Delivered,

    /// <summary>The notification could not be created; see <see cref="MedicineOrderStatusChange.NotificationError"/>.</summary>
    Failed
}

/// <summary>
/// One order status change made by pharmacy staff through payment management, with the Kafka event that announced it
/// and whether the patient was notified.
/// </summary>
public class MedicineOrderStatusChange
{
    public int Id { get; set; }

    public int MedicinePaymentId { get; set; }

    public int OrderId { get; set; }

    public Guid PatientId { get; set; }

    [MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string FromStatus { get; set; } = string.Empty;

    [MaxLength(30)]
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Required for a rejection; shown to the patient.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    public Guid ChangedBy { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    /// <summary>The Kafka event (outbox entry) for this change.</summary>
    public Guid EventId { get; set; }

    public PatientNotificationStatus NotificationStatus { get; set; }

    [MaxLength(500)]
    public string? NotificationError { get; set; }

    public DateTime? NotifiedAtUtc { get; set; }
}
