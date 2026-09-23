namespace DoctorPayment.Api.DTOs.DoctorAvailability;

/// <summary>One consultation slot as the owning doctor sees it (booked slots are included; patients only see open ones).</summary>
public class AvailabilitySlotResponseDto
{
    public int Id { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public bool IsBooked { get; set; }
}
