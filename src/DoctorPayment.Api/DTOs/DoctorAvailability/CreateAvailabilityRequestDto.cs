namespace DoctorPayment.Api.DTOs.DoctorAvailability;

/// <summary>
/// A window of time the doctor is available, split into consecutive consultation slots of
/// <see cref="SlotDurationMinutes"/> each. Any remainder shorter than one slot is dropped.
/// </summary>
public class CreateAvailabilityRequestDto
{
    public const int MinSlotMinutes = 10;
    public const int MaxSlotMinutes = 240;
    public const int MaxWindowHours = 12;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public int SlotDurationMinutes { get; set; } = 20;
}
