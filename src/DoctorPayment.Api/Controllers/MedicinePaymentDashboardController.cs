using System.Data.Common;
using System.Security.Claims;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

[ApiController]
public sealed class MedicinePaymentDashboardController(MedicinePaymentDashboardService service) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpGet("api/admin/dashboard/medicine-payments")]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        try { return Ok(await service.GetAdminAsync(ct)); }
        catch (DbException) { return StatusCode(503, new { message = "Payment information is unavailable. Please retry." }); }
    }

    [Authorize(Policy = AuthorizationPolicies.Patient)]
    [HttpGet("api/patient/dashboard/medicine-payments")]
    public async Task<IActionResult> Patient(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty) return Unauthorized();
        try { return Ok(await service.GetPatientAsync(id, ct)); }
        catch (DbException) { return StatusCode(503, new { message = "Payment information is unavailable. Please retry." }); }
    }

    [Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
    [HttpGet("api/staff/dashboard/medicine-payments")]
    public async Task<IActionResult> Staff(CancellationToken ct)
    {
        try { return Ok(await service.GetStaffAsync(ct)); }
        catch (DbException) { return StatusCode(503, new { message = "Payment information is unavailable. Please retry." }); }
    }
}
