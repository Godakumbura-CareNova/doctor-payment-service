using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class DoctorNotificationRepository : IDoctorNotificationRepository
{
    private readonly AppDbContext _dbContext;

    public DoctorNotificationRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<int?> GetProfileIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles
            .AsNoTracking()
            .Where(profile => profile.DoctorId == doctorId)
            .Select(profile => (int?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<List<DoctorNotification>> GetRecentAsync(int profileId, int take, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorNotifications
            .AsNoTracking()
            .Where(notification => notification.DoctorProfileId == profileId)
            .OrderByDescending(notification => notification.CreatedAtUtc).ThenByDescending(notification => notification.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<DoctorNotification?> GetAsync(int profileId, int notificationId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorNotifications
            .SingleOrDefaultAsync(notification => notification.Id == notificationId && notification.DoctorProfileId == profileId, cancellationToken);

    public Task<List<DoctorNotification>> GetUnreadAsync(int profileId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorNotifications
            .Where(notification => notification.DoctorProfileId == profileId && !notification.IsRead)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);
}
