using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace DoctorPayment.Api.Tests.Integration;

public class MedicinePaymentDashboardAccessTests
{
    private const string Secret = "gcn51-integration-test-secret-at-least-32-characters";
    internal sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string name = Guid.NewGuid().ToString();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("JwtSettings:Secret", Secret);
            builder.UseSetting("JwtSettings:Issuer", "GCN51");
            builder.UseSetting("JwtSettings:Audience", "GCN51");
            builder.ConfigureServices(services =>
            {
                foreach (var item in services.Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(IHostedService) && (d.ImplementationType == typeof(OutboxPublisherService) || d.ImplementationType == typeof(KafkaConsumerService))).ToList()) services.Remove(item);
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
            });
        }
    }

    [Theory]
    [InlineData(null, "/api/staff/dashboard/medicine-payments", HttpStatusCode.Unauthorized)]
    [InlineData("Patient", "/api/staff/dashboard/medicine-payments", HttpStatusCode.Forbidden)]
    [InlineData("Admin", "/api/staff/dashboard/medicine-payments", HttpStatusCode.Forbidden)]
    [InlineData("Doctor", "/api/patient/dashboard/medicine-payments", HttpStatusCode.Forbidden)]
    [InlineData("PharmacyStaff", "/api/staff/dashboard/medicine-payments", HttpStatusCode.OK)]
    [InlineData("Pharmacy Staff", "/api/staff/dashboard/medicine-payments", HttpStatusCode.OK)]
    public async Task Dashboard_EnforcesRole(string? role, string path, HttpStatusCode expected)
    {
        using var factory = new Factory(); using var client = Client(factory, role, Guid.NewGuid());
        Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task PatientDashboard_CannotRequestAnotherPatientViaQueryString()
    {
        using var factory = new Factory(); var mine = Guid.NewGuid(); var other = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MedicinePayments.AddRange(new MedicinePayment { PatientId = mine, OrderId = 1, OrderNumber = "OWN", Status = MedicinePaymentStatus.PayOnCollection },
                new MedicinePayment { PatientId = other, OrderId = 2, OrderNumber = "PRIVATE", Status = MedicinePaymentStatus.Paid });
            await db.SaveChangesAsync();
        }
        using var client = Client(factory, "Patient", mine);
        var response = await client.GetAsync($"/api/patient/dashboard/medicine-payments?patientId={other}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("OWN", Assert.Single(json.RootElement.GetProperty("recentPayments").EnumerateArray()).GetProperty("reference").GetString());
    }

    internal static HttpClient Client(Factory factory, string? role, Guid patient)
    {
        var client = factory.CreateClient();
        if (role != null)
        {
            var token = new JwtSecurityToken("GCN51", "GCN51", new[] { new Claim(JwtRegisteredClaimNames.Sub, patient.ToString()), new Claim(ClaimTypes.Role, role) },
                expires: DateTime.UtcNow.AddHours(1), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }
}
