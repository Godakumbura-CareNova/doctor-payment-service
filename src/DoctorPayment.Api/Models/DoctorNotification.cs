using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>An in-app message for a doctor, shown under the bell in the dashboard (e.g. a fee request was approved).</summary>
public class DoctorNotification
{
    public int Id { get; set; }

    public int DoctorProfileId { get; set; }

    public DoctorProfile? DoctorProfile { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>What the notification is about, e.g. "FeeChangeRequest", so the UI can link to it.</summary>
    [MaxLength(50)]
    public string? RelatedEntityType { get; set; }

    public int? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
