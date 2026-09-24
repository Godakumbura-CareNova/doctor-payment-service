using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

public enum FeeChangeRequestStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// A doctor's request to change their consultation fee. Doctors cannot change the fee directly;
/// it only changes when an admin approves one of these.
/// </summary>
public class ConsultationFeeChangeRequest
{
    public int Id { get; set; }

    public int DoctorProfileId { get; set; }

    public DoctorProfile? DoctorProfile { get; set; }

    /// <summary>The fee when the request was submitted (null when none was set), so the admin sees the change asked for.</summary>
    public decimal? CurrentFee { get; set; }

    public decimal RequestedFee { get; set; }

    public FeeChangeRequestStatus Status { get; set; } = FeeChangeRequestStatus.Pending;

    /// <summary>
    /// Equals <see cref="DoctorProfileId"/> while the request is pending and null once it is reviewed.
    /// A unique index on it lets the database refuse a second pending request for the same doctor,
    /// even when two submissions race (MySQL has no filtered indexes, but ignores NULLs in unique ones).
    /// </summary>
    public int? PendingDoctorProfileId { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime SubmittedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>User id of the admin who approved or rejected the request.</summary>
    public Guid? ReviewedByAdminId { get; set; }
}
