namespace DoctorPayment.Api.DTOs.DoctorSearch;

/// <summary>Whether a doctor currently has open consultation slots, and when the next one is.</summary>
public class DoctorAvailabilityDto
{
    public bool HasAvailableSlots { get; set; }

    public int AvailableSlotCount { get; set; }

    public DateTime? NextAvailableSlotUtc { get; set; }
}
