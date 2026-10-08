using System.Net;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Services.Interfaces;
using DoctorPayment.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
namespace DoctorPayment.Api.Tests.Integration;
public class PublicDoctorsIntegrationTests
{
    [Fact]
    public async Task AnonymousVisitorCanUseSharedDirectoryButCannotEditDoctorProfiles()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IDoctorSearchService>();
        service.Setup(s => s.SearchAsync(It.IsAny<DoctorSearchQueryDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DoctorSearchResultDto { Items = [new() { DoctorId = id, FullName = "Dr Silva" }] });
        service.Setup(s => s.GetPublicProfileAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new DoctorPublicProfileDto { DoctorId = id, FullName = "Dr Silva" });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("JwtSettings:Secret", "integration-test-secret-with-at-least-32-characters");
            builder.UseSetting("JwtSettings:Issuer", "CareNova");
            builder.UseSetting("JwtSettings:Audience", "CareNova");
            builder.ConfigureServices(services =>
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType == typeof(OutboxPublisherService) || d.ImplementationType == typeof(KafkaConsumerService))).ToList()) services.Remove(hosted);
                services.AddSingleton(service.Object);
            });
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/public/doctors")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/doctors/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/public/doctors/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/doctors")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/doctors/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/doctors/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/public/doctors?page=0")).StatusCode);
    }
}
