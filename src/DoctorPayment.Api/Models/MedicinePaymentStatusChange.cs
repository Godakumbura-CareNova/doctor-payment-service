using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>One change of a medicine payment's status: who made it, when, and why.</summary>
public class MedicinePaymentStatusChange
{
    public int Id { get; set; }

    public int MedicinePaymentId { get; set; }

    /// <summary>Null for the first entry, when the payment method was chosen.</summary>
    public MedicinePaymentStatus? FromStatus { get; set; }

    public MedicinePaymentStatus ToStatus { get; set; }

    public Guid ChangedBy { get; set; }

    /// <summary>"Patient", "PharmacyStaff" or "Admin".</summary>
    [MaxLength(30)]
    public string ChangedByRole { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
