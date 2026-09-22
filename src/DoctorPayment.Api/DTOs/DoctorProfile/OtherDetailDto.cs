namespace DoctorPayment.Api.DTOs.DoctorProfile;

public class OtherDetailDto
{
    public string Title { get; set; } = string.Empty;

    public string? Details { get; set; }

    public int StartYear { get; set; }

    public int? EndYear { get; set; }
}
