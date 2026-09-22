using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

public interface IDoctorProfileRepository
{
    Task<DoctorProfile?> GetByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default);

    Task AddAsync(DoctorProfile profile, CancellationToken cancellationToken = default);

    void Remove(DoctorProfile profile);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
