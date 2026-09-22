namespace DoctorPayment.Api.DTOs.DoctorProfile;

/// <summary>Full replacement of the caller's professional profile (create or update).</summary>
public class UpdateDoctorProfileRequestDto
{
    public string Specialization { get; set; } = string.Empty;

    public string RegistrationNumber { get; set; } = string.Empty;

    public string? Biography { get; set; }

    public List<QualificationDto> Qualifications { get; set; } = new();

    public List<ExperienceDto> Experiences { get; set; } = new();

    /// <summary>Optional section: memberships, awards, publications, training and similar.</summary>
    public List<OtherDetailDto> OtherDetails { get; set; } = new();
}
