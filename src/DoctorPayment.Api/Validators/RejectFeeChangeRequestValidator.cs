using DoctorPayment.Api.DTOs.FeeChangeRequest;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

/// <summary>A rejection must say why, since the doctor sees the reason in their notification.</summary>
public class RejectFeeChangeRequestValidator : AbstractValidator<RejectFeeChangeRequestDto>
{
    public RejectFeeChangeRequestValidator()
    {
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Enter a reason for rejecting this request.")
            .Must(reason => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length >= RejectFeeChangeRequestDto.MinReasonLength)
            .WithMessage($"The reason must be at least {RejectFeeChangeRequestDto.MinReasonLength} characters.")
            .Must(reason => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length <= RejectFeeChangeRequestDto.MaxReasonLength)
            .WithMessage($"The reason must not exceed {RejectFeeChangeRequestDto.MaxReasonLength} characters.");
    }
}
