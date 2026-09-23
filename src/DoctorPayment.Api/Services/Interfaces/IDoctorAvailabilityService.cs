using DoctorPayment.Api.DTOs.DoctorAvailability;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IDoctorAvailabilityService
{
    /// <summary>The caller's upcoming slots, soonest first. Empty when there is no profile or no slots.</summary>
    Task<List<AvailabilitySlotResponseDto>> GetMineAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Splits the window into consecutive slots and stores them. Throws
    /// <see cref="Exceptions.DoctorProfileRequiredException"/> when the doctor has no profile,
    /// <see cref="ArgumentException"/> for a window in the past or too far ahead, and
    /// <see cref="Exceptions.AvailabilityConflictException"/> when it overlaps existing slots or exceeds the slot cap.
    /// </summary>
    Task<List<AvailabilitySlotResponseDto>> AddAsync(Guid doctorId, CreateAvailabilityRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves or resizes one open slot. Returns null when the slot does not exist or belongs to another doctor;
    /// throws <see cref="ArgumentException"/> for a time in the past or too far ahead, and
    /// <see cref="Exceptions.AvailabilityConflictException"/> when the slot is booked or the new time overlaps another slot.
    /// </summary>
    Task<AvailabilitySlotResponseDto?> UpdateAsync(Guid doctorId, int slotId, UpdateAvailabilitySlotRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one open slot. Returns false when the slot does not exist or belongs to another doctor;
    /// throws <see cref="Exceptions.AvailabilityConflictException"/> when the slot is already booked.
    /// </summary>
    Task<bool> DeleteAsync(Guid doctorId, int slotId, CancellationToken cancellationToken = default);
}
