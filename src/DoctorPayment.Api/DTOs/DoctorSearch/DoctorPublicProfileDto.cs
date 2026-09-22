using DoctorPayment.Api.DTOs.DoctorProfile;

namespace DoctorPayment.Api.DTOs.DoctorSearch;

/// <summary>The professional profile as patients see it: everything the doctor published plus availability.</summary>
public class DoctorPublicProfileDto
{
    public Guid DoctorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Specialization { get; set; } = string.Empty;

    public string RegistrationNumber { get; set; } = string.Empty;

    public string? Biography { get; set; }

    public List<QualificationResponseDto> Qualifications { get; set; } = new();

    public List<ExperienceResponseDto> Experiences { get; set; } = new();

    public List<OtherDetailResponseDto> OtherDetails { get; set; } = new();

    public int? YearsOfExperience { get; set; }

    public DoctorAvailabilityDto Availability { get; set; } = new();

    /// <summary>The next few open consultation windows, soonest first.</summary>
    public List<AvailabilitySlotDto> UpcomingSlots { get; set; } = new();

    public DateTime UpdatedAtUtc { get; set; }
}

public class AvailabilitySlotDto
{
    public int Id { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }
}
