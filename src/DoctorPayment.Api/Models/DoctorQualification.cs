using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

public class DoctorQualification
{
    public int Id { get; set; }

    public int DoctorProfileId { get; set; }

    public DoctorProfile? DoctorProfile { get; set; }

    [MaxLength(100)]
    public string Degree { get; set; } = string.Empty;

    [MaxLength(150)]
    public string Institution { get; set; } = string.Empty;

    public int StartYear { get; set; }

    /// <summary>Null while the qualification is still in progress.</summary>
    public int? EndYear { get; set; }
}
