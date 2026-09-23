namespace DoctorPayment.Api.DTOs.DoctorProfile;

/// <summary>Full replacement of the caller's professional profile (create or update).</summary>
public class UpdateDoctorProfileRequestDto
{
    public string Specialization { get; set; } = string.Empty;

    public string RegistrationNumber { get; set; } = string.Empty;

    public string? Biography { get; set; }

    /// <summary>Path of the doctor's picture on the user-auth service; null when they have not uploaded one.</summary>
    public string? ProfilePicturePath { get; set; }

    /// <summary>Consultation fee in LKR; null when the doctor has not set one.</summary>
    public decimal? ConsultationFee { get; set; }

    public List<QualificationDto> Qualifications { get; set; } = new();

    public List<ExperienceDto> Experiences { get; set; } = new();

    /// <summary>Optional section: memberships, awards, publications, training and similar.</summary>
    public List<OtherDetailDto> OtherDetails { get; set; } = new();
}
