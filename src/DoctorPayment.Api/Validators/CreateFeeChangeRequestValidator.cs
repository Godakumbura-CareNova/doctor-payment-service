using DoctorPayment.Api.DTOs.FeeChangeRequest;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

/// <summary>
/// The requested fee must be present, positive, in whole cents and not absurdly large.
/// "Same as the current fee" and "already pending" need the database, so the service checks those.
/// </summary>
public class CreateFeeChangeRequestValidator : AbstractValidator<CreateFeeChangeRequestDto>
{
    public CreateFeeChangeRequestValidator()
    {
        RuleFor(request => request.RequestedFee)
            .NotNull().WithMessage("Enter the new consultation fee.");

        When(request => request.RequestedFee.HasValue, () =>
        {
            RuleFor(request => request.RequestedFee!.Value)
                .GreaterThan(0m).WithMessage("Consultation fee must be greater than 0.")
                .LessThanOrEqualTo(CreateFeeChangeRequestDto.MaxFee)
                .WithMessage($"Consultation fee must not exceed {CreateFeeChangeRequestDto.MaxFee:N0}.")
                .Must(fee => decimal.Round(fee, 2) == fee).WithMessage("Consultation fee can have at most 2 decimal places.")
                .OverridePropertyName(nameof(CreateFeeChangeRequestDto.RequestedFee));
        });
    }
}
