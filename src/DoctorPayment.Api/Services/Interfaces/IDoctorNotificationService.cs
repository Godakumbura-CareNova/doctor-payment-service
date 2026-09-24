using DoctorPayment.Api.DTOs.DoctorNotification;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IDoctorNotificationService
{
    /// <summary>The doctor's latest notifications, newest first. Empty when there is no profile.</summary>
    Task<List<DoctorNotificationDto>> GetMineAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>Marks one notification read. False when it does not exist or belongs to another doctor.</summary>
    Task<bool> MarkReadAsync(Guid doctorId, int notificationId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid doctorId, CancellationToken cancellationToken = default);
}
