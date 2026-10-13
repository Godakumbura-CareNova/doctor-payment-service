using System.Net.Http.Json;
using System.Text.Json;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DoctorPayment.Api.Tests.Integration;
public class AdminMedicineReportsTests
{
    private const string Path = "/api/admin/reports/medicine-payments?from=2026-09-30T18:30:00Z&to=2026-10-01T18:30:00Z";
    [Theory]
    [InlineData(null, 401)] [InlineData("Patient", 403)] [InlineData("Doctor", 403)] [InlineData("PharmacyStaff", 403)] [InlineData("Admin", 200)]
    public async Task OnlyAdmin(string? role, int status)
    {
        using var factory = new MedicinePaymentDashboardAccessTests.Factory();
        using var client = MedicinePaymentDashboardAccessTests.Client(factory, role, Guid.NewGuid());
        Assert.Equal(status, (int)(await client.GetAsync(Path)).StatusCode);
        if (role == "Admin") Assert.Equal(200, (int)(await client.GetAsync("/api/admin/reports/medicine-payments?from=1999-12-26T18:30:00Z&to=2000-01-02T18:30:00Z")).StatusCode);
    }
    [Fact]
    public async Task UsesAuthoritativePaidDatesExclusiveEndAndStablePaging()
    {
        using var factory = new MedicinePaymentDashboardAccessTests.Factory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var starts = new[] { new DateTime(2026, 9, 30, 18, 29, 59, DateTimeKind.Utc), new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 18, 29, 59, DateTimeKind.Utc), new DateTime(2026, 10, 1, 18, 30, 0, DateTimeKind.Utc) };
            for (var i = 1; i <= 6; i++)
            {
                var p = new MedicinePayment { Id = i, OrderId = i, PatientId = Guid.NewGuid(), Amount = 100, Status = i == 6 ? MedicinePaymentStatus.Failed : MedicinePaymentStatus.Paid };
                if (i != 5) p.StatusHistory.Add(new() { FromStatus = MedicinePaymentStatus.PayOnCollection, ToStatus = MedicinePaymentStatus.Paid, ChangedAtUtc = starts[Math.Min(i - 1, 3)] });
                db.Add(p);
            }
            await db.SaveChangesAsync();
        }
        using var client = MedicinePaymentDashboardAccessTests.Client(factory, "Admin", Guid.NewGuid());
        var response = await client.GetAsync(Path + "&pageSize=1");
        Assert.Equal(200, (int)response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, json.GetProperty("undatedSuccessfulCount").GetInt32());
        Assert.Equal(2, Assert.Single(json.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetInt32());
        Assert.Equal(400, (int)(await client.GetAsync(Path + "&pageSize=101")).StatusCode);
        Assert.Equal(400, (int)(await client.GetAsync("/api/admin/reports/medicine-payments?from=2026-01-01Z&to=2026-12-01Z")).StatusCode);
    }
}
