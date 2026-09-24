using DoctorPayment.Api.DTOs.DoctorNotification;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;

namespace DoctorPayment.Api.Services;

public class DoctorNotificationService : IDoctorNotificationService
{
    /// <summary>How many notifications the bell shows.</summary>
    public const int MaxListed = 50;

    private readonly IDoctorNotificationRepository _repository;

    public DoctorNotificationService(IDoctorNotificationRepository repository) => _repository = repository;

    public async Task<List<DoctorNotificationDto>> GetMineAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profileId = await _repository.GetProfileIdAsync(doctorId, cancellationToken);
        if (profileId is null) return new List<DoctorNotificationDto>();

        return (await _repository.GetRecentAsync(profileId.Value, MaxListed, cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<bool> MarkReadAsync(Guid doctorId, int notificationId, CancellationToken cancellationToken = default)
    {
        var profileId = await _repository.GetProfileIdAsync(doctorId, cancellationToken);
        if (profileId is null) return false;

        var notification = await _repository.GetAsync(profileId.Value, notificationId, cancellationToken);
        if (notification is null) return false;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _repository.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    public async Task MarkAllReadAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profileId = await _repository.GetProfileIdAsync(doctorId, cancellationToken);
        if (profileId is null) return;

        var unread = await _repository.GetUnreadAsync(profileId.Value, cancellationToken);
        if (unread.Count == 0) return;

        unread.ForEach(notification => notification.IsRead = true);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static DoctorNotificationDto ToResponse(DoctorNotification notification) => new()
    {
        Id = notification.Id,
        Title = notification.Title,
        Message = notification.Message,
        RelatedEntityType = notification.RelatedEntityType,
        RelatedEntityId = notification.RelatedEntityId,
        IsRead = notification.IsRead,
        CreatedAtUtc = DateTime.SpecifyKind(notification.CreatedAtUtc, DateTimeKind.Utc)
    };
}
