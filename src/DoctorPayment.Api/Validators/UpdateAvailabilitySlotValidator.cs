using DoctorPayment.Api.DTOs.DoctorAvailability;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

/// <summary>
/// Shape rules for editing one slot. As with <see cref="CreateAvailabilityValidator"/>, the clock-dependent
/// and overlap checks live in the service.
/// </summary>
public class UpdateAvailabilitySlotValidator : AbstractValidator<UpdateAvailabilitySlotRequestDto>
{
    public UpdateAvailabilitySlotValidator()
    {
        RuleFor(request => request.StartUtc)
            .NotEqual(default(DateTime)).WithMessage("Start time is required.");

        RuleFor(request => request.EndUtc)
            .NotEqual(default(DateTime)).WithMessage("End time is required.")
            .GreaterThan(request => request.StartUtc).WithMessage("End time must be after the start time.")
            .Must((request, end) => end <= request.StartUtc || end - request.StartUtc >= TimeSpan.FromMinutes(CreateAvailabilityRequestDto.MinSlotMinutes))
            .WithMessage($"A slot must be at least {CreateAvailabilityRequestDto.MinSlotMinutes} minutes long.")
            .Must((request, end) => end - request.StartUtc <= TimeSpan.FromMinutes(CreateAvailabilityRequestDto.MaxSlotMinutes))
            .WithMessage($"A slot cannot be longer than {CreateAvailabilityRequestDto.MaxSlotMinutes} minutes.");
    }
}
