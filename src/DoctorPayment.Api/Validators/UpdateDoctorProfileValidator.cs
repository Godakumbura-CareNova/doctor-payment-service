using DoctorPayment.Api.DTOs.DoctorProfile;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

public class UpdateDoctorProfileValidator : AbstractValidator<UpdateDoctorProfileRequestDto>
{
    public const int MinYear = 1950;
    public const int MaxEntries = 20;

    public UpdateDoctorProfileValidator()
    {
        RuleFor(profile => profile.Specialization)
            .NotEmpty().WithMessage("Specialization is required.")
            .MinimumLength(2).WithMessage("Specialization must be at least 2 characters.")
            .MaximumLength(100).WithMessage("Specialization must not exceed 100 characters.");

        RuleFor(profile => profile.RegistrationNumber)
            .NotEmpty().WithMessage("Medical registration number is required.")
            .Matches(@"^[A-Za-z0-9/\-]{3,50}$")
            .WithMessage("Medical registration number must be 3 to 50 letters, digits, slashes or hyphens.");

        RuleFor(profile => profile.Biography)
            .MaximumLength(2000).WithMessage("Biography must not exceed 2000 characters.");

        RuleFor(profile => profile.Qualifications)
            .NotNull().WithMessage("Qualifications must be provided.")
            .Must(items => items is null || items.Count <= MaxEntries)
            .WithMessage($"At most {MaxEntries} qualifications can be recorded.");

        RuleFor(profile => profile.Experiences)
            .NotNull().WithMessage("Experiences must be provided.")
            .Must(items => items is null || items.Count <= MaxEntries)
            .WithMessage($"At most {MaxEntries} experience entries can be recorded.");

        RuleFor(profile => profile.OtherDetails)
            .NotNull().WithMessage("Other details must be provided.")
            .Must(items => items is null || items.Count <= MaxEntries)
            .WithMessage($"At most {MaxEntries} other details can be recorded.");

        RuleForEach(profile => profile.Qualifications).SetValidator(new QualificationValidator());
        RuleForEach(profile => profile.Experiences).SetValidator(new ExperienceValidator());
        RuleForEach(profile => profile.OtherDetails).SetValidator(new OtherDetailValidator());
    }

    private static int CurrentYear => DateTime.UtcNow.Year;

    private sealed class QualificationValidator : AbstractValidator<QualificationDto>
    {
        public QualificationValidator()
        {
            RuleFor(item => item.Degree)
                .NotEmpty().WithMessage("Degree is required.")
                .MinimumLength(2).WithMessage("Degree must be at least 2 characters.")
                .MaximumLength(100).WithMessage("Degree must not exceed 100 characters.");
            RuleFor(item => item.Institution)
                .NotEmpty().WithMessage("Institution is required.")
                .MinimumLength(2).WithMessage("Institution must be at least 2 characters.")
                .MaximumLength(150).WithMessage("Institution must not exceed 150 characters.");
            RuleFor(item => item.StartYear)
                .InclusiveBetween(MinYear, CurrentYear)
                .WithMessage($"Start year must be between {MinYear} and {CurrentYear}.");
            RuleFor(item => item.EndYear)
                .Must((item, endYear) => IsValidEndYear(item.StartYear, endYear))
                .WithMessage($"End year must be on or after the start year and not later than {CurrentYear}.");
        }
    }

    private sealed class ExperienceValidator : AbstractValidator<ExperienceDto>
    {
        public ExperienceValidator()
        {
            RuleFor(item => item.Position)
                .NotEmpty().WithMessage("Position is required.")
                .MinimumLength(2).WithMessage("Position must be at least 2 characters.")
                .MaximumLength(100).WithMessage("Position must not exceed 100 characters.");
            RuleFor(item => item.Organization)
                .NotEmpty().WithMessage("Organization is required.")
                .MinimumLength(2).WithMessage("Organization must be at least 2 characters.")
                .MaximumLength(150).WithMessage("Organization must not exceed 150 characters.");
            RuleFor(item => item.StartYear)
                .InclusiveBetween(MinYear, CurrentYear)
                .WithMessage($"Start year must be between {MinYear} and {CurrentYear}.");
            RuleFor(item => item.EndYear)
                .Must((item, endYear) => IsValidEndYear(item.StartYear, endYear))
                .WithMessage($"End year must be on or after the start year and not later than {CurrentYear}.");
        }
    }

    private sealed class OtherDetailValidator : AbstractValidator<OtherDetailDto>
    {
        public OtherDetailValidator()
        {
            RuleFor(item => item.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MinimumLength(2).WithMessage("Title must be at least 2 characters.")
                .MaximumLength(100).WithMessage("Title must not exceed 100 characters.");
            RuleFor(item => item.Details)
                .MaximumLength(300).WithMessage("Details must not exceed 300 characters.");
            RuleFor(item => item.StartYear)
                .InclusiveBetween(MinYear, CurrentYear)
                .WithMessage($"Start year must be between {MinYear} and {CurrentYear}.");
            RuleFor(item => item.EndYear)
                .Must((item, endYear) => IsValidEndYear(item.StartYear, endYear))
                .WithMessage($"End year must be on or after the start year and not later than {CurrentYear}.");
        }
    }

    // A null end year means "in progress" / "present"; otherwise it must fall inside [StartYear, CurrentYear].
    private static bool IsValidEndYear(int startYear, int? endYear) =>
        endYear is null || (endYear >= startYear && endYear <= CurrentYear);
}
