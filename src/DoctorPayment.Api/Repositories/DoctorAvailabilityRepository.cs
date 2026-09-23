using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class DoctorAvailabilityRepository : IDoctorAvailabilityRepository
{
    private readonly AppDbContext _dbContext;

    public DoctorAvailabilityRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<int?> GetProfileIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles
            .AsNoTracking()
            .Where(profile => profile.DoctorId == doctorId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<List<DoctorAvailabilitySlot>> GetUpcomingSlotsAsync(int profileId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorAvailabilitySlots
            .AsNoTracking()
            .Where(slot => slot.DoctorProfileId == profileId && slot.EndUtc > nowUtc)
            .OrderBy(slot => slot.StartUtc).ThenBy(slot => slot.Id)
            .ToListAsync(cancellationToken);

    public Task<List<DoctorAvailabilitySlot>> GetOverlappingSlotsAsync(int profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorAvailabilitySlots
            .AsNoTracking()
            .Where(slot => slot.DoctorProfileId == profileId && slot.StartUtc < endUtc && slot.EndUtc > startUtc)
            .OrderBy(slot => slot.StartUtc)
            .ToListAsync(cancellationToken);

    public Task<DoctorAvailabilitySlot?> GetSlotAsync(int profileId, int slotId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorAvailabilitySlots
            .SingleOrDefaultAsync(slot => slot.Id == slotId && slot.DoctorProfileId == profileId, cancellationToken);

    public Task AddRangeAsync(IEnumerable<DoctorAvailabilitySlot> slots, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorAvailabilitySlots.AddRangeAsync(slots, cancellationToken);

    public void Remove(DoctorAvailabilitySlot slot) => _dbContext.DoctorAvailabilitySlots.Remove(slot);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);
}
