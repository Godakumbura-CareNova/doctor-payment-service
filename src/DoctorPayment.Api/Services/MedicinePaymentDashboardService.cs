using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.Dashboard;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Services;

public sealed class MedicinePaymentDashboardService(AppDbContext db, TimeProvider clock)
{
    public async Task<PatientMedicinePaymentDashboard> GetPatientAsync(Guid patientId, CancellationToken ct)
    {
        var query = db.MedicinePayments.AsNoTracking().Where(p => p.PatientId == patientId);
        var counts = await query.GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var rows = await Project(query.OrderByDescending(p => p.UpdatedAtUtc).ThenByDescending(p => p.Id).Take(5)).ToListAsync(ct);
        return new(counts.ToDictionary(c => c.Status.ToString(), c => c.Count), rows.Select(ToRow).ToArray(), clock.GetUtcNow().UtcDateTime);
    }

    public async Task<StaffMedicinePaymentDashboard> GetStaffAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
        var day = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), zone);
        var month = TimeZoneInfo.ConvertTimeToUtc(new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), zone);
        var pending = db.MedicinePayments.AsNoTracking().Where(p => p.Method == PaymentMethod.BankTransfer
            && p.Status == MedicinePaymentStatus.ReceiptUnderVerification
            && p.Receipts.Any(r => r.Status == BankTransferReceiptStatus.PendingVerification));
        var pendingCount = await pending.CountAsync(ct);
        var pendingRows = await Project(pending.OrderBy(p => p.UpdatedAtUtc).ThenBy(p => p.Id).Take(5)).ToListAsync(ct);
        var paid = Project(db.MedicinePayments.AsNoTracking().Where(p => p.Status == MedicinePaymentStatus.Paid));
        var dated = paid.Where(p => p.ConfirmedAtUtc != null && p.ConfirmedAtUtc <= now);
        var todayTotal = await dated.Where(p => p.ConfirmedAtUtc >= day).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var monthTotal = await dated.Where(p => p.ConfirmedAtUtc >= month).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var undated = paid.Where(p => p.ConfirmedAtUtc == null);
        var undatedCount = await undated.CountAsync(ct);
        var undatedTotal = await undated.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var recent = await dated.OrderByDescending(p => p.ConfirmedAtUtc).ThenByDescending(p => p.Id).Take(5).ToListAsync(ct);
        return new(new(pendingCount, pendingRows.Select(ToRow).ToArray()), recent.Select(ToRow).ToArray(),
            new(todayTotal, monthTotal, undatedCount, undatedTotal, "LKR", "Asia/Colombo", now), now);
    }

    private static IQueryable<PaymentProjection> Project(IQueryable<MedicinePayment> query) => query.Select(p => new PaymentProjection
    {
        Id = p.Id, OrderId = p.OrderId, Reference = p.OrderNumber, Amount = p.Amount, Method = p.Method,
        Status = p.Status, UpdatedAtUtc = p.UpdatedAtUtc,
        ConfirmedAtUtc = p.StatusHistory.Where(h => h.ToStatus == MedicinePaymentStatus.Paid && h.FromStatus != MedicinePaymentStatus.Paid)
            .Select(h => (DateTime?)h.ChangedAtUtc).Max()
    });

    private static PaymentDashboardRow ToRow(PaymentProjection p) => new(p.Id, p.OrderId, p.Reference, p.Amount,
        p.Method.ToString(), p.Status.ToString(), p.ConfirmedAtUtc.HasValue ? DateTime.SpecifyKind(p.ConfirmedAtUtc.Value, DateTimeKind.Utc) : null,
        DateTime.SpecifyKind(p.UpdatedAtUtc, DateTimeKind.Utc));

    private sealed class PaymentProjection
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Reference { get; set; } = "";
        public decimal Amount { get; set; }
        public PaymentMethod Method { get; set; }
        public MedicinePaymentStatus Status { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? ConfirmedAtUtc { get; set; }
    }
}
