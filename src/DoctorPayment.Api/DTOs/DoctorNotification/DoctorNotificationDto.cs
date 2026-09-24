namespace DoctorPayment.Api.DTOs.DoctorNotification;

/// <summary>Same shape as the pharmacy service's notifications, so the dashboard bell can show either.</summary>
public class DoctorNotificationDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? RelatedEntityType { get; set; }

    public int? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
