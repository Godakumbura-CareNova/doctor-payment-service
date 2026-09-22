using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

public class DoctorExperience
{
    public int Id { get; set; }

    public int DoctorProfileId { get; set; }

    public DoctorProfile? DoctorProfile { get; set; }

    [MaxLength(100)]
    public string Position { get; set; } = string.Empty;

    [MaxLength(150)]
    public string Organization { get; set; } = string.Empty;

    public int StartYear { get; set; }

    /// <summary>Null while the doctor still holds the position.</summary>
    public int? EndYear { get; set; }
}
