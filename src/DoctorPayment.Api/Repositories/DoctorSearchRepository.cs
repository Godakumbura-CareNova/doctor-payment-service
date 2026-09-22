using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class DoctorSearchRepository : IDoctorSearchRepository
{
    private readonly AppDbContext _dbContext;

    public DoctorSearchRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<(List<DoctorSearchRow> Items, int TotalCount)> SearchAsync(
        string? search,
        string? specialization,
        bool availableOnly,
        DateTime nowUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<DoctorProfile> profiles = _dbContext.DoctorProfiles.AsNoTracking();

        // ToLower() on both sides keeps matching case-insensitive on every provider (MySQL and the in-memory test provider).
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            profiles = profiles.Where(profile =>
                profile.FullName.ToLower().Contains(term) ||
                profile.Specialization.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(specialization))
        {
            var wanted = specialization.Trim().ToLower();
            profiles = profiles.Where(profile => profile.Specialization.ToLower() == wanted);
        }

        var rows = profiles.Select(profile => new
        {
            Profile = profile,
            Degrees = profile.Qualifications.OrderBy(q => q.StartYear).ThenBy(q => q.Id).Select(q => q.Degree).ToList(),
            FirstExperienceYear = profile.Experiences.Min(e => (int?)e.StartYear),
            AvailableSlotCount = profile.AvailabilitySlots.Count(slot => !slot.IsBooked && slot.StartUtc > nowUtc),
            NextAvailableSlotUtc = profile.AvailabilitySlots
                .Where(slot => !slot.IsBooked && slot.StartUtc > nowUtc)
                .Min(slot => (DateTime?)slot.StartUtc)
        });

        if (availableOnly)
        {
            rows = rows.Where(row => row.AvailableSlotCount > 0);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var pageRows = await rows
            .OrderByDescending(row => row.AvailableSlotCount > 0)
            .ThenBy(row => row.Profile.FullName)
            .ThenBy(row => row.Profile.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = pageRows
            .Select(row => new DoctorSearchRow(row.Profile, row.Degrees, row.FirstExperienceYear, row.AvailableSlotCount, row.NextAvailableSlotUtc))
            .ToList();

        return (items, totalCount);
    }

    public Task<DoctorProfile?> GetPublicProfileAsync(Guid doctorId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles
            .AsNoTracking()
            .Include(profile => profile.Qualifications)
            .Include(profile => profile.Experiences)
            .Include(profile => profile.OtherDetails)
            .Include(profile => profile.AvailabilitySlots.Where(slot => !slot.IsBooked && slot.StartUtc > nowUtc))
            .SingleOrDefaultAsync(profile => profile.DoctorId == doctorId, cancellationToken);

    public Task<List<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles
            .AsNoTracking()
            .Where(profile => profile.Specialization != "")
            .Select(profile => profile.Specialization)
            .Distinct()
            .OrderBy(specialization => specialization)
            .ToListAsync(cancellationToken);
}
