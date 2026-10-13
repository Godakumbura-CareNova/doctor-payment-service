using System.Net.Http.Json;
using System.Text.Json;

namespace DoctorPayment.Api.Tests.Integration;

public class AdminMedicinePaymentDashboardAccessTests
{
    [Theory]
    [InlineData(null, 401)] [InlineData("Patient", 403)] [InlineData("User", 403)] [InlineData("Doctor", 403)]
    [InlineData("PharmacyStaff", 403)] [InlineData("Pharmacy Staff", 403)] [InlineData("Admin", 200)]
    public async Task AdminOnlyAndEmpty(string? role, int status)
    {
        using var factory = new MedicinePaymentDashboardAccessTests.Factory();
        using var client = MedicinePaymentDashboardAccessTests.Client(factory, role, Guid.NewGuid());
        var response = await client.GetAsync("/api/admin/dashboard/medicine-payments");
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, json.GetProperty("totalPayments").GetInt32());
            Assert.Equal(0, json.GetProperty("income").GetProperty("todayAmount").GetDecimal());
        }
    }
}
