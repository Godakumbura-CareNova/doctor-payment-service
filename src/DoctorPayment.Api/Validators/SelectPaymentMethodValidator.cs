using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Services;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

/// <summary>A method must be chosen and be one we know. Whether it is offered for this order is the service's call.</summary>
public class SelectPaymentMethodValidator : AbstractValidator<SelectPaymentMethodRequestDto>
{
    public SelectPaymentMethodValidator()
    {
        RuleFor(request => request.Method)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Select a payment method.")
            .Must(method => MedicinePaymentService.TryParseMethod(method, out _))
            .WithMessage("Choose online payment, bank transfer or cash.");
    }
}
