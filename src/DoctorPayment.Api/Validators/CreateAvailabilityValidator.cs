using DoctorPayment.Api.DTOs.DoctorAvailability;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

/// <summary>
/// Shape rules for an availability window. Clock-dependent rules (must be in the future, not too far ahead)
/// and overlap checks live in the service, where the time provider is injectable.
/// </summary>
public class CreateAvailabilityValidator : AbstractValidator<CreateAvailabilityRequestDto>
{
    public CreateAvailabilityValidator()
    {
        RuleFor(request => request.StartUtc)
            .NotEqual(default(DateTime)).WithMessage("Start time is required.");

        RuleFor(request => request.EndUtc)
            .NotEqual(default(DateTime)).WithMessage("End time is required.")
            .GreaterThan(request => request.StartUtc).WithMessage("End time must be after the start time.")
            .Must((request, end) => end - request.StartUtc <= TimeSpan.FromHours(CreateAvailabilityRequestDto.MaxWindowHours))
            .WithMessage($"An availability window cannot be longer than {CreateAvailabilityRequestDto.MaxWindowHours} hours.");

        RuleFor(request => request.SlotDurationMinutes)
            .InclusiveBetween(CreateAvailabilityRequestDto.MinSlotMinutes, CreateAvailabilityRequestDto.MaxSlotMinutes)
            .WithMessage($"Slot length must be between {CreateAvailabilityRequestDto.MinSlotMinutes} and {CreateAvailabilityRequestDto.MaxSlotMinutes} minutes.")
            .Must((request, minutes) => request.EndUtc <= request.StartUtc || TimeSpan.FromMinutes(minutes) <= request.EndUtc - request.StartUtc)
            .WithMessage("Slot length must fit inside the availability window.");
    }
}
