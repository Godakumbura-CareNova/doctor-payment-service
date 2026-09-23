using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>Slot storage for a doctor managing their own availability.</summary>
public interface IDoctorAvailabilityRepository
{
    /// <summary>Internal profile id for the doctor, or null when no profile has been created.</summary>
    Task<int?> GetProfileIdAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>Every slot that has not ended yet, soonest first (booked ones included).</summary>
    Task<List<DoctorAvailabilitySlot>> GetUpcomingSlotsAsync(int profileId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Slots that intersect [startUtc, endUtc); touching edges do not count as overlapping.</summary>
    Task<List<DoctorAvailabilitySlot>> GetOverlappingSlotsAsync(int profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    Task<DoctorAvailabilitySlot?> GetSlotAsync(int profileId, int slotId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<DoctorAvailabilitySlot> slots, CancellationToken cancellationToken = default);

    void Remove(DoctorAvailabilitySlot slot);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
