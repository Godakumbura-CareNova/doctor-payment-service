namespace DoctorPayment.Api.DTOs.DoctorProfile;

public class ExperienceDto
{
    public string Position { get; set; } = string.Empty;

    public string Organization { get; set; } = string.Empty;

    public int StartYear { get; set; }

    public int? EndYear { get; set; }
}
