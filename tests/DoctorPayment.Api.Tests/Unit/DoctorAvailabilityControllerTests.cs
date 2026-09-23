using System.Security.Claims;
using DoctorPayment.Api.Controllers;
using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using DoctorPayment.Api.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class DoctorAvailabilityControllerTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private readonly Mock<IDoctorAvailabilityService> _service = new();

    [Fact]
    public void Controller_RequiresDoctorPolicy()
    {
        var attribute = typeof(DoctorAvailabilityController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.Doctor, attribute.Policy);
    }

    [Fact]
    public async Task GetMine_ReturnsTheCallersSlots()
    {
        var doctorId = Guid.NewGuid();
        var slots = new List<AvailabilitySlotResponseDto> { new() { Id = 1, StartUtc = Start, EndUtc = Start.AddMinutes(20) } };
        _service.Setup(s => s.GetMineAsync(doctorId, It.IsAny<CancellationToken>())).ReturnsAsync(slots);
        var controller = CreateController(doctorId);

        var result = await controller.GetMine(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(slots, ok.Value);
    }

    [Fact]
    public async Task GetMine_ReturnsUnauthorized_WhenTokenHasNoSubject()
    {
        var controller = CreateController(doctorId: null);

        var result = await controller.GetMine(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        _service.Verify(s => s.GetMineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_ReturnsBadRequest_AndDoesNotCallService_WhenInvalid()
    {
        var controller = CreateController(Guid.NewGuid());
        var request = new CreateAvailabilityRequestDto { StartUtc = Start, EndUtc = Start, SlotDurationMinutes = 20 };

        var result = await controller.Add(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _service.Verify(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<CreateAvailabilityRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_ReturnsCreated_WithTheNewSlots()
    {
        var doctorId = Guid.NewGuid();
        var request = ValidRequest();
        var created = new List<AvailabilitySlotResponseDto> { new() { Id = 5, StartUtc = Start, EndUtc = Start.AddMinutes(20) } };
        _service.Setup(s => s.AddAsync(doctorId, request, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        var controller = CreateController(doctorId);

        var result = await controller.Add(request, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Same(created, response.Value);
    }

    [Fact]
    public async Task Add_MapsPastWindow_ToBadRequest()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.AddAsync(doctorId, It.IsAny<CreateAvailabilityRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Availability must start in the future."));
        var controller = CreateController(doctorId);

        var result = await controller.Add(ValidRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Add_MapsMissingProfile_ToConflict()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.AddAsync(doctorId, It.IsAny<CreateAvailabilityRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DoctorProfileRequiredException());
        var controller = CreateController(doctorId);

        var result = await controller.Add(ValidRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Add_MapsOverlap_ToConflict()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.AddAsync(doctorId, It.IsAny<CreateAvailabilityRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AvailabilityConflictException("overlap"));
        var controller = CreateController(doctorId);

        var result = await controller.Add(ValidRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenRemoved()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.DeleteAsync(doctorId, 9, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var controller = CreateController(doctorId);

        Assert.IsType<NoContentResult>(await controller.Delete(9, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenUnknown()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.DeleteAsync(doctorId, 9, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var controller = CreateController(doctorId);

        Assert.IsType<NotFoundObjectResult>(await controller.Delete(9, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ReturnsConflict_WhenSlotIsBooked()
    {
        var doctorId = Guid.NewGuid();
        _service.Setup(s => s.DeleteAsync(doctorId, 9, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AvailabilityConflictException("booked"));
        var controller = CreateController(doctorId);

        Assert.IsType<ConflictObjectResult>(await controller.Delete(9, CancellationToken.None));
    }

    private DoctorAvailabilityController CreateController(Guid? doctorId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, AuthorizationPolicies.Doctor), new(ClaimTypes.Name, "Dr Test") };
        if (doctorId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, doctorId.Value.ToString()));

        return new DoctorAvailabilityController(_service.Object, new CreateAvailabilityValidator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
            }
        };
    }

    private static CreateAvailabilityRequestDto ValidRequest() =>
        new() { StartUtc = Start, EndUtc = Start.AddHours(1), SlotDurationMinutes = 20 };
}
