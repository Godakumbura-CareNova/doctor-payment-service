namespace DoctorPayment.Api.DTOs.DoctorAvailability;

/// <summary>
/// New start and end for one existing consultation slot. Unlike <see cref="CreateAvailabilityRequestDto"/>
/// this moves or resizes a single slot rather than splitting a window.
/// </summary>
public class UpdateAvailabilitySlotRequestDto
{
    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }
}
