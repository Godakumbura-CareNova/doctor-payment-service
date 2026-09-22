using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>
/// Professional profile owned by one doctor account. Identity fields (name, email, phone)
/// live in the user-auth service; this entity only stores professional information.
/// </summary>
public class DoctorProfile
{
    public int Id { get; set; }

    /// <summary>User id issued by the user-auth service (JWT subject).</summary>
    public Guid DoctorId { get; set; }

    /// <summary>
    /// Snapshot of the doctor's name from the user-auth JWT, refreshed every time the doctor saves the profile.
    /// Kept here so patients can search doctors by name without this service calling user-auth.
    /// </summary>
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Specialization { get; set; } = string.Empty;

    [MaxLength(50)]
    public string RegistrationNumber { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Biography { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<DoctorQualification> Qualifications { get; set; } = new();

    public List<DoctorExperience> Experiences { get; set; } = new();

    public List<DoctorOtherDetail> OtherDetails { get; set; } = new();

    public List<DoctorAvailabilitySlot> AvailabilitySlots { get; set; } = new();
}
