namespace DoctorPayment.Api.DTOs.DoctorProfile;

public class DoctorProfileResponseDto
{
    public int Id { get; set; }

    public Guid DoctorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Specialization { get; set; } = string.Empty;

    public string RegistrationNumber { get; set; } = string.Empty;

    public string? Biography { get; set; }

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
