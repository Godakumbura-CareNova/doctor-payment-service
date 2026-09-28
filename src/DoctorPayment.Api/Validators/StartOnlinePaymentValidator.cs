using System.Text.RegularExpressions;
using DoctorPayment.Api.DTOs.MedicinePayment;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

/// <summary>GCN-41: the billing details PayHere requires, checked before the payment starts so the patient sees what to fix.</summary>
public partial class StartOnlinePaymentValidator : AbstractValidator<StartOnlinePaymentRequestDto>
{
    public StartOnlinePaymentValidator()
    {
        RuleFor(request => request.FirstName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter your first name.")
            .MaximumLength(50).WithMessage("Keep your first name to 50 characters or fewer.")
            .Matches(NamePattern()).WithMessage("Your first name can only contain letters, spaces, dots, apostrophes and hyphens.");

        RuleFor(request => request.LastName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter your last name.")
            .MaximumLength(50).WithMessage("Keep your last name to 50 characters or fewer.")
            .Matches(NamePattern()).WithMessage("Your last name can only contain letters, spaces, dots, apostrophes and hyphens.");

        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter your email address.")
            .MaximumLength(100).WithMessage("Keep your email address to 100 characters or fewer.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(request => request.Phone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter your phone number.")
            .Matches(PhonePattern()).WithMessage("Enter a valid Sri Lankan phone number, e.g. 0771234567 or +94771234567.");

        RuleFor(request => request.Address)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter your address.")
            .MaximumLength(100).WithMessage("Keep your address to 100 characters or fewer.");

        RuleFor(request => request.City)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter your city.")
            .MaximumLength(50).WithMessage("Keep your city to 50 characters or fewer.")
            .Matches(NamePattern()).WithMessage("Your city can only contain letters, spaces, dots, apostrophes and hyphens.");
    }

    [GeneratedRegex(@"^\s*\p{L}[\p{L} .'-]*\s*$")]
    private static partial Regex NamePattern();

    /// <summary>0XXXXXXXXX or +94XXXXXXXXX, spaces and hyphens allowed.</summary>
    [GeneratedRegex(@"^\s*(\+94|0094|94|0)[ -]?\d{2}[ -]?\d{3}[ -]?\d{4}\s*$")]
    private static partial Regex PhonePattern();
}
