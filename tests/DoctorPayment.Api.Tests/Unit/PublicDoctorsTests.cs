using DoctorPayment.Api.Controllers;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;
namespace DoctorPayment.Api.Tests.Unit;
public class PublicDoctorsTests
{
    [Fact]
    public async Task DirectoryReturnsOnlySelectedPublicFields()
    {
        var service = new Mock<IDoctorSearchService>();
        service.Setup(s => s.SearchAsync(It.IsAny<DoctorSearchQueryDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DoctorSearchResultDto { Items = [new() { FullName = "Dr Silva", RegistrationNumber = "internal", Specialization = "General medicine" }] });
        var response = Assert.IsType<OkObjectResult>(await new PublicDoctorsController(service.Object).List(new(), default));
        var json = JsonSerializer.Serialize(response.Value);
        Assert.Contains("Dr Silva", json);
        Assert.DoesNotContain("RegistrationNumber", json);
    }
    [Fact]
    public async Task MissingDoctorReturnsNotFound()
    {
        var service = new Mock<IDoctorSearchService>();
        Assert.IsType<NotFoundObjectResult>(await new PublicDoctorsController(service.Object).Detail(Guid.NewGuid(), default));
    }
    [Fact]
    public async Task InvalidPaginationReturnsBadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(await new PublicDoctorsController(Mock.Of<IDoctorSearchService>()).List(new() { Page = 0 }, default));
    }
}
