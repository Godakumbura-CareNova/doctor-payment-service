using System.Security.Claims;
using DoctorPayment.Api.Controllers;
using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using DoctorPayment.Api.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class DoctorProfileControllerTests
{
    private readonly Mock<IDoctorProfileService> _service = new();

    [Fact]
    public void Controller_RequiresDoctorPolicy()
    {
        var attribute = typeof(DoctorProfileController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.Doctor, attribute.Policy);
    }

    [Fact]
    public async Task GetMine_ReturnsNotFound_WhenProfileMissing()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.GetByDoctorIdAsync(doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DoctorProfileResponseDto?)null);
        var controller = CreateController(doctorId);

        var result = await controller.GetMine(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMine_ReturnsProfile_WhenItExists()
    {
        var doctorId = Guid.NewGuid();
        var profile = new DoctorProfileResponseDto { Id = 7, DoctorId = doctorId, Specialization = "GP" };
        _service.Setup(s => s.GetByDoctorIdAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var controller = CreateController(doctorId);

        var result = await controller.GetMine(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(profile, ok.Value);
    }

    [Fact]
    public async Task GetMine_ReturnsUnauthorized_WhenTokenHasNoSubject()
    {
        var controller = CreateController(doctorId: null);

        var result = await controller.GetMine(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        _service.Verify(s => s.GetByDoctorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveMine_ReturnsBadRequest_AndDoesNotSave_WhenInvalid()
    {
        var doctorId = Guid.NewGuid();
        var controller = CreateController(doctorId);
        var request = ValidRequest();
        request.Specialization = "";

        var result = await controller.SaveMine(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _service.Verify(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<UpdateDoctorProfileRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveMine_ReturnsSavedProfile_WhenValid()
    {
        var doctorId = Guid.NewGuid();
        var request = ValidRequest();
        var saved = new DoctorProfileResponseDto { Id = 3, DoctorId = doctorId, Specialization = request.Specialization };
        _service.Setup(s => s.SaveAsync(doctorId, It.IsAny<string?>(), request, It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        var controller = CreateController(doctorId);

        var result = await controller.SaveMine(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(saved, ok.Value);
    }

    [Fact]
    public async Task SaveMine_UsesTokenSubject_NeverABodySuppliedId()
    {
        var tokenDoctorId = Guid.NewGuid();
        Guid? capturedDoctorId = null;
        _service.Setup(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<UpdateDoctorProfileRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, UpdateDoctorProfileRequestDto, CancellationToken>((id, _, _, _) => capturedDoctorId = id)
            .ReturnsAsync(new DoctorProfileResponseDto { DoctorId = tokenDoctorId });
        var controller = CreateController(tokenDoctorId);

        await controller.SaveMine(ValidRequest(), CancellationToken.None);

        Assert.Equal(tokenDoctorId, capturedDoctorId);
    }

    [Fact]
    public async Task DeleteMine_ReturnsNoContent_WhenProfileDeleted()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.DeleteAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var controller = CreateController(doctorId);

        var result = await controller.DeleteMine(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteMine_ReturnsNotFound_WhenNothingToDelete()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.DeleteAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var controller = CreateController(doctorId);

        var result = await controller.DeleteMine(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private DoctorProfileController CreateController(Guid? doctorId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, AuthorizationPolicies.Doctor) };
        if (doctorId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, doctorId.Value.ToString()));

        return new DoctorProfileController(_service.Object, new UpdateDoctorProfileValidator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                }
            }
        };
    }

    private static UpdateDoctorProfileRequestDto ValidRequest() => new()
    {
        Specialization = "General Practitioner",
        RegistrationNumber = "SLMC-12345",
        Qualifications = new List<QualificationDto>
        {
            new() { Degree = "MBBS", Institution = "University of Colombo", StartYear = 2010, EndYear = 2015 }
        },
        Experiences = new List<ExperienceDto>()
    };
}
