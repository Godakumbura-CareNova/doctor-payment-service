namespace DoctorPayment.Api.DTOs.DoctorProfile;

public class QualificationDto
{
    public string Degree { get; set; } = string.Empty;

    public string Institution { get; set; } = string.Empty;

    public int StartYear { get; set; }

    public int? EndYear { get; set; }
}
