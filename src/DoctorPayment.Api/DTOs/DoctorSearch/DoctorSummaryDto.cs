namespace DoctorPayment.Api.DTOs.DoctorSearch;

/// <summary>One row in the doctor search results: enough to pick a doctor, not the whole profile.</summary>
public class DoctorSummaryDto
{
    public Guid DoctorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Specialization { get; set; } = string.Empty;

    public string RegistrationNumber { get; set; } = string.Empty;

    public string? Biography { get; set; }

    /// <summary>Path of the doctor's picture on the user-auth service; null when they have not uploaded one.</summary>
    public string? ProfilePicturePath { get; set; }

    /// <summary>Consultation fee in LKR; null when the doctor has not set one.</summary>
    public decimal? ConsultationFee { get; set; }

    /// <summary>Degrees in the order they were earned, e.g. ["MBBS", "MD"].</summary>
    public List<string> Qualifications { get; set; } = new();

    /// <summary>Years since the doctor's earliest recorded position, or null when no experience is recorded.</summary>
    public int? YearsOfExperience { get; set; }

    public DoctorAvailabilityDto Availability { get; set; } = new();
}
