using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>A doctor reading and dismissing their own notifications.</summary>
public interface IDoctorNotificationRepository
{
    /// <summary>Internal profile id for the doctor, or null when no profile has been created.</summary>
    Task<int?> GetProfileIdAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>The doctor's latest notifications, newest first.</summary>
    Task<List<DoctorNotification>> GetRecentAsync(int profileId, int take, CancellationToken cancellationToken = default);

    /// <summary>One notification (tracked), only when it belongs to this doctor.</summary>
    Task<DoctorNotification?> GetAsync(int profileId, int notificationId, CancellationToken cancellationToken = default);

    Task<List<DoctorNotification>> GetUnreadAsync(int profileId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
