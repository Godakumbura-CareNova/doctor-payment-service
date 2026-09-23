using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.Validators;
using FluentValidation.TestHelper;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class CreateAvailabilityValidatorTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private readonly CreateAvailabilityValidator _validator = new();

    [Fact]
    public void ValidWindow_Passes()
    {
        var request = new CreateAvailabilityRequestDto { StartUtc = Start, EndUtc = Start.AddHours(2), SlotDurationMinutes = 20 };

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void MissingTimes_Fail()
    {
        var request = new CreateAvailabilityRequestDto { SlotDurationMinutes = 20 };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(r => r.StartUtc);
        result.ShouldHaveValidationErrorFor(r => r.EndUtc);
    }

    [Fact]
    public void EndBeforeOrEqualToStart_Fails()
    {
        var request = new CreateAvailabilityRequestDto { StartUtc = Start, EndUtc = Start, SlotDurationMinutes = 20 };

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.EndUtc);
    }

    [Fact]
    public void WindowLongerThanTwelveHours_Fails()
    {
        var request = new CreateAvailabilityRequestDto { StartUtc = Start, EndUtc = Start.AddHours(12).AddMinutes(1), SlotDurationMinutes = 20 };

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.EndUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(241)]
    public void SlotDurationOutsideRange_Fails(int minutes)
    {
        var request = new CreateAvailabilityRequestDto { StartUtc = Start, EndUtc = Start.AddHours(8), SlotDurationMinutes = minutes };

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.SlotDurationMinutes);
    }

    [Fact]
    public void SlotLongerThanTheWindow_Fails()
    {
        var request = new CreateAvailabilityRequestDto { StartUtc = Start, EndUtc = Start.AddMinutes(15), SlotDurationMinutes = 30 };

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.SlotDurationMinutes);
    }
}
