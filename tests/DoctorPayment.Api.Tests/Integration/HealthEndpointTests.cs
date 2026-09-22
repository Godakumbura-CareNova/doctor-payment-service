using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DoctorPayment.Api.Tests.Integration;

public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.HealthApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(HealthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body);
    }

    /// <summary>Supplies the non-secret settings Program.cs requires so the API can boot without user secrets.</summary>
    public sealed class HealthApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("JwtSettings:Secret", "health-test-jwt-secret-that-is-at-least-32-characters");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;User=unused;Password=unused;");
        }
    }
}
