using System.Security.Claims;
using DoctorPayment.Api.DTOs.DoctorNotification;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>The doctor's in-app notifications (e.g. fee request approved or rejected), shown under the dashboard bell.</summary>
[ApiController]
[Route("api/doctors/me/notifications")]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
public class DoctorNotificationsController : ControllerBase
{
    private readonly IDoctorNotificationService _notificationService;

    public DoctorNotificationsController(IDoctorNotificationService notificationService) => _notificationService = notificationService;

    [HttpGet]
    public async Task<ActionResult<List<DoctorNotificationDto>>> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        return Ok(await _notificationService.GetMineAsync(doctorId, cancellationToken));
    }

    [HttpPut("{notificationId:int}/read")]
    public async Task<IActionResult> MarkRead(int notificationId, CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        return await _notificationService.MarkReadAsync(doctorId, notificationId, cancellationToken)
            ? NoContent()
            : NotFound(new { message = "That notification no longer exists." });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        await _notificationService.MarkAllReadAsync(doctorId, cancellationToken);
        return NoContent();
    }

    private bool TryGetDoctorId(out Guid doctorId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out doctorId) && doctorId != Guid.Empty;
}
