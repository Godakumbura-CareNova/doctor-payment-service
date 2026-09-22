using DoctorPayment.Api.Controllers;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using DoctorPayment.Api.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class DoctorsControllerTests
{
    private readonly Mock<IDoctorSearchService> _service = new();

    [Fact]
    public void Controller_RequiresPatientPolicy()
    {
        var attribute = typeof(DoctorsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.Patient, attribute.Policy);
    }

    [Fact]
    public async Task Search_ReturnsResults_ForValidCriteria()
    {
        var query = new DoctorSearchQueryDto { Search = "Nimal" };
        var expected = new DoctorSearchResultDto { TotalCount = 1, Items = { new DoctorSummaryDto { FullName = "Nimal Perera" } } };
        _service.Setup(s => s.SearchAsync(query, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await CreateController().Search(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task Search_ReturnsEmptyResult_WhenNothingMatches()
    {
        var query = new DoctorSearchQueryDto { Search = "Nobody" };
        _service.Setup(s => s.SearchAsync(query, It.IsAny<CancellationToken>())).ReturnsAsync(new DoctorSearchResultDto());

        var result = await CreateController().Search(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsType<DoctorSearchResultDto>(ok.Value).Items);
    }

    [Fact]
    public async Task Search_ReturnsBadRequest_AndDoesNotSearch_WhenCriteriaInvalid()
    {
        var query = new DoctorSearchQueryDto { Search = "<script>" };

        var result = await CreateController().Search(query, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _service.Verify(s => s.SearchAsync(It.IsAny<DoctorSearchQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProfile_ReturnsProfile_WhenPublished()
    {
        var doctorId = Guid.NewGuid();
        var profile = new DoctorPublicProfileDto { DoctorId = doctorId, FullName = "Nimal Perera" };
        _service.Setup(s => s.GetPublicProfileAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        var result = await CreateController().GetProfile(doctorId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(profile, ok.Value);
    }

    [Fact]
    public async Task GetProfile_ReturnsNotFound_WhenDoctorHasNoProfile()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.GetPublicProfileAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync((DoctorPublicProfileDto?)null);

        var result = await CreateController().GetProfile(doctorId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Specializations_ReturnsTheList()
    {
        _service.Setup(s => s.GetSpecializationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "Cardiologist" });

        var result = await CreateController().Specializations(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(new[] { "Cardiologist" }, Assert.IsType<List<string>>(ok.Value));
    }

    private DoctorsController CreateController() => new(_service.Object, new DoctorSearchQueryValidator());
}
