namespace DoctorPayment.Api.Models;

/// <summary>
/// One bookable consultation window offered by a doctor. Doctor search only reads these
/// (to show whether a doctor has open slots); creating and managing them is the availability story.
/// </summary>
public class DoctorAvailabilitySlot
{
    public int Id { get; set; }

    public int DoctorProfileId { get; set; }

    public DoctorProfile? DoctorProfile { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>True once a patient has taken the slot; booked slots no longer count as available.</summary>
    public bool IsBooked { get; set; }
}
