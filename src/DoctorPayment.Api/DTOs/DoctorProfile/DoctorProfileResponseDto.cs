namespace DoctorPayment.Api.DTOs.DoctorProfile;

public class DoctorProfileResponseDto
{
    public int Id { get; set; }

    public Guid DoctorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Specialization { get; set; } = string.Empty;

    public string RegistrationNumber { get; set; } = string.Empty;

    public string? Biography { get; set; }

    /// <summary>Path of the doctor's picture on the user-auth service; null when they have not uploaded one.</summary>
    public string? ProfilePicturePath { get; set; }

    /// <summary>Consultation fee in LKR; null when the doctor has not set one.</summary>
    public decimal? ConsultationFee { get; set; }

    public List<QualificationResponseDto> Qualifications { get; set; } = new();

    public List<ExperienceResponseDto> Experiences { get; set; } = new();

    public List<OtherDetailResponseDto> OtherDetails { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public class QualificationResponseDto : QualificationDto
{
    public int Id { get; set; }
}

public class ExperienceResponseDto : ExperienceDto
{
    public int Id { get; set; }
}

public class OtherDetailResponseDto : OtherDetailDto
{
    public int Id { get; set; }
}
