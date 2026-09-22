using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

/// <summary>
/// Any other professional detail a doctor wants to show (memberships, awards, publications, training...).
/// The whole section is optional; a profile may have none.
/// </summary>
public class DoctorOtherDetail
{
    public int Id { get; set; }

    public int DoctorProfileId { get; set; }

    public DoctorProfile? DoctorProfile { get; set; }

    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Details { get; set; }

    public int StartYear { get; set; }

    /// <summary>Null means "Present" (still ongoing).</summary>
    public int? EndYear { get; set; }
}
