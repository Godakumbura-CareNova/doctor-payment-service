using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Controllers;
[ApiController]
[Route("api/admin/reports/medicine-payments")]
[Authorize(Roles = "Admin")]
public sealed class AdminMedicineReportsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(DateTimeOffset from, DateTimeOffset to, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        // A selectable January 2000 period can start in UTC December 1999 (including Monday-based weeks).
        if (from.Year < 1999 || to <= from || to - from > TimeSpan.FromDays(32) || page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { message = "Choose a reporting period of at most 32 days and valid page values." });
        var start = from.UtcDateTime; var end = to.UtcDateTime; var now = clock.GetUtcNow().UtcDateTime;
        var paid = db.MedicinePayments.AsNoTracking().Where(p => p.Status == MedicinePaymentStatus.Paid)
            .Select(p => new { p.Id, p.OrderId, p.PatientId, p.Amount,
                ConfirmedAtUtc = p.StatusHistory.Where(h => h.ToStatus == MedicinePaymentStatus.Paid && h.FromStatus != MedicinePaymentStatus.Paid)
                    .Select(h => (DateTime?)h.ChangedAtUtc).Max() });
        var undatedSuccessfulCount = await paid.CountAsync(p => p.ConfirmedAtUtc == null, ct);
        var query = paid.Where(p => p.ConfirmedAtUtc >= start && p.ConfirmedAtUtc < end && p.ConfirmedAtUtc <= now);
        var totalCount = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var items = rows.Select(p => new { p.Id, p.OrderId, p.PatientId, p.Amount, ConfirmedAtUtc = DateTime.SpecifyKind(p.ConfirmedAtUtc!.Value, DateTimeKind.Utc) });
        return Ok(new { items, totalCount, page, pageSize, totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            undatedSuccessfulCount, currency = "LKR", timeZone = "Asia/Colombo", generatedAtUtc = now });
    }
}
