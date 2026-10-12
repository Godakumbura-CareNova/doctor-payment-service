using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DoctorPayment.Api.Tests.Unit;

public class AdminMedicinePaymentDashboardTests
{
    [Fact]
    public async Task CountsAllPaymentsAndReusesConfirmedIncomeWithStableFiveRows()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 11; i++)
        {
            var p = new MedicinePayment { Id = i, OrderId = i, PatientId = Guid.NewGuid(), OrderNumber = $"CN-{i}", Amount = 100,
                Status = i == 11 ? MedicinePaymentStatus.Failed : MedicinePaymentStatus.Paid, Method = PaymentMethod.Cash, CreatedAtUtc = now, UpdatedAtUtc = now };
            if (i != 10) p.StatusHistory.Add(new() { FromStatus = MedicinePaymentStatus.PayOnCollection, ToStatus = MedicinePaymentStatus.Paid,
                ChangedAtUtc = i == 8 ? new DateTime(2026, 10, 31, 18, 29, 59, DateTimeKind.Utc) : i == 9 ? now.AddDays(1) : now });
            if (i == 1) p.StatusHistory.Add(new() { FromStatus = MedicinePaymentStatus.Paid, ToStatus = MedicinePaymentStatus.Paid, ChangedAtUtc = now.AddMinutes(5) });
            db.Add(p);
        }
        await db.SaveChangesAsync();
        var service = new MedicinePaymentDashboardService(db, new FakeTimeProvider(new DateTimeOffset(now)));
        var result = await service.GetAdminAsync(default);
        Assert.Equal(11, result.TotalPayments); Assert.Equal(10, result.StatusCounts["Paid"]); Assert.Equal(1, result.StatusCounts["Failed"]);
        Assert.Equal(700, result.Income.TodayAmount); Assert.Equal(700, result.Income.MonthAmount);
        Assert.Equal(1, result.Income.UndatedSuccessfulCount); Assert.Equal(100, result.Income.UndatedSuccessfulAmount);
        Assert.Equal(new[] { 7, 6, 5, 4, 3 }, result.RecentPayments.Select(p => p.Id));
        Assert.Equal((await service.GetStaffAsync(default)).Income, result.Income);
    }
}
