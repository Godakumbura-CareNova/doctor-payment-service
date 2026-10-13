using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DoctorPayment.Api.Tests.Unit;

public class MedicinePaymentDashboardTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc);
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static MedicinePayment Payment(int order, Guid patient, MedicinePaymentStatus status = MedicinePaymentStatus.Paid) =>
        new() { OrderId = order, PatientId = patient, OrderNumber = $"CN-{order}", Amount = 100, Status = status, Method = PaymentMethod.Cash, CreatedAtUtc = Now, UpdatedAtUtc = Now };
    private static void Confirm(MedicinePayment p, DateTime time, MedicinePaymentStatus? from = MedicinePaymentStatus.PayOnCollection) =>
        p.StatusHistory.Add(new() { FromStatus = from, ToStatus = MedicinePaymentStatus.Paid, ChangedAtUtc = time });

    [Fact]
    public async Task PatientSummary_IsOwned_WithFullCountsAndFivePreviews()
    {
        await using var db = Db(); var patient = Guid.NewGuid();
        for (var i = 1; i <= 8; i++) db.MedicinePayments.Add(Payment(i, i == 8 ? Guid.NewGuid() : patient));
        await db.SaveChangesAsync();
        var result = await new MedicinePaymentDashboardService(db, new FakeTimeProvider(new DateTimeOffset(Now))).GetPatientAsync(patient, default);
        Assert.Equal(7, result.StatusCounts["Paid"]);
        Assert.Equal(5, result.RecentPayments.Count);
        Assert.DoesNotContain(result.RecentPayments, p => p.OrderId == 8);
    }

    [Fact]
    public async Task Income_UsesSriLankaConfirmationBoundaries_NotCreatedOrUpdatedDates()
    {
        await using var db = Db(); var patient = Guid.NewGuid();
        var times = new[] { new DateTime(2026, 10, 31, 18, 29, 59, DateTimeKind.Utc), new DateTime(2026, 10, 31, 18, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 18, 30, 0, DateTimeKind.Utc) };
        for (var i = 0; i < times.Length; i++) { var p = Payment(i + 1, patient); Confirm(p, times[i]); db.Add(p); }
        var legacy = Payment(4, patient); db.Add(legacy);
        var failed = Payment(5, patient, MedicinePaymentStatus.Failed); Confirm(failed, Now); db.Add(failed);
        await db.SaveChangesAsync();
        var service = new MedicinePaymentDashboardService(db, new FakeTimeProvider(new DateTimeOffset(Now)));
        var result = await service.GetStaffAsync(default);
        Assert.Equal(100m, result.Income.TodayAmount);
        Assert.Equal(100m, result.Income.MonthAmount); // Future confirmation is not yet income.
        Assert.Equal(1, result.Income.UndatedSuccessfulCount);
        Assert.Equal(100m, result.Income.UndatedSuccessfulAmount);
        Assert.Equal("Asia/Colombo", result.Income.TimeZone);
    }

    [Fact]
    public async Task Income_DoesNotDoubleCountRepeatedPaidHistory_AndUsesCurrentSuccessfulPeriod()
    {
        await using var db = Db(); var patient = Guid.NewGuid();
        var p = Payment(1, patient); Confirm(p, Now.AddDays(-3));
        p.StatusHistory.Add(new() { FromStatus = MedicinePaymentStatus.Paid, ToStatus = MedicinePaymentStatus.Failed, ChangedAtUtc = Now.AddDays(-2) });
        Confirm(p, Now.AddMinutes(-30), MedicinePaymentStatus.Failed);
        Confirm(p, Now, MedicinePaymentStatus.Paid); // Repeated success must not establish another period.
        db.Add(p); await db.SaveChangesAsync();
        var result = await new MedicinePaymentDashboardService(db, new FakeTimeProvider(new DateTimeOffset(Now))).GetStaffAsync(default);
        Assert.Equal(100m, result.Income.TodayAmount);
        Assert.Equal(Now.AddMinutes(-30), Assert.Single(result.RecentPayments).ConfirmedAtUtc);
    }

    [Fact]
    public async Task TransferQueue_RequiresUploadedPendingReceipt_AndCountsBeyondPreview()
    {
        await using var db = Db(); var patient = Guid.NewGuid();
        for (var i = 1; i <= 8; i++)
        {
            var p = Payment(i, patient, MedicinePaymentStatus.ReceiptUnderVerification); p.Method = PaymentMethod.BankTransfer;
            if (i != 8) p.Receipts.Add(new() { PatientId = patient, OrderId = i, Status = BankTransferReceiptStatus.PendingVerification, UploadedAtUtc = Now });
            db.Add(p);
        }
        await db.SaveChangesAsync();
        var result = await new MedicinePaymentDashboardService(db, new FakeTimeProvider(new DateTimeOffset(Now))).GetStaffAsync(default);
        Assert.Equal(7, result.PendingTransfers.TotalCount);
        Assert.Equal(5, result.PendingTransfers.Items.Count);
        Assert.Equal(0m, result.Income.TodayAmount);
    }
}
