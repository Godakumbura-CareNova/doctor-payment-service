using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class DoctorProfileRepository : IDoctorProfileRepository
{
    private readonly AppDbContext _dbContext;

    public DoctorProfileRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<DoctorProfile?> GetByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles
            .Include(profile => profile.Qualifications)
            .Include(profile => profile.Experiences)
            .Include(profile => profile.OtherDetails)
            .SingleOrDefaultAsync(profile => profile.DoctorId == doctorId, cancellationToken);

    public async Task AddAsync(DoctorProfile profile, CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorProfiles.AddAsync(profile, cancellationToken);

    public void Remove(DoctorProfile profile) => _dbContext.DoctorProfiles.Remove(profile);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
