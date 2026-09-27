using System.Security.Claims;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Controllers;

/// <summary>The signed-in patient's notifications created from Kafka events (e.g. their order is ready for collection).</summary>
[ApiController]
[Route("api/patient/notifications")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class PatientNotificationsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public PatientNotificationsController(AppDbContext dbContext) => _dbContext = dbContext;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Please sign in again." });

        var notifications = await _dbContext.PatientNotifications.AsNoTracking()
            .Where(notification => notification.PatientId == patientId)
            .OrderByDescending(notification => notification.CreatedAtUtc).ThenByDescending(notification => notification.Id)
            .Take(50)
            .Select(notification => new
            {
                notification.Id,
                notification.Title,
                notification.Message,
                notification.OrderId,
                notification.OrderStatus,
                notification.IsRead,
                CreatedAtUtc = DateTime.SpecifyKind(notification.CreatedAtUtc, DateTimeKind.Utc)
            })
            .ToListAsync(cancellationToken);
        return Ok(notifications);
    }

    [HttpPut("{notificationId:int}/read")]
    public async Task<IActionResult> MarkRead(int notificationId, CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Please sign in again." });

        var notification = await _dbContext.PatientNotifications
            .SingleOrDefaultAsync(item => item.Id == notificationId && item.PatientId == patientId, cancellationToken);
        if (notification is null) return NotFound(new { message = "That notification no longer exists." });
        notification.IsRead = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Please sign in again." });

        var unread = await _dbContext.PatientNotifications.Where(item => item.PatientId == patientId && !item.IsRead).ToListAsync(cancellationToken);
        unread.ForEach(item => item.IsRead = true);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private bool TryGetPatientId(out Guid patientId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out patientId) && patientId != Guid.Empty;
}
