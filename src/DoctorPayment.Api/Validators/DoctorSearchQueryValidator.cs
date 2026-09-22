using DoctorPayment.Api.DTOs.DoctorSearch;
using FluentValidation;

namespace DoctorPayment.Api.Validators;

public class DoctorSearchQueryValidator : AbstractValidator<DoctorSearchQueryDto>
{
    public const int MinSearchLength = 2;
    public const int MaxSearchLength = 100;

    // Names and specializations: letters (any script), digits, spaces and the punctuation that appears in them.
    private const string SearchPattern = @"^[\p{L}\p{N} .,'&/()\-]+$";

    public DoctorSearchQueryValidator()
    {
        When(query => !string.IsNullOrWhiteSpace(query.Search), () =>
        {
            RuleFor(query => query.Search!.Trim())
                .MinimumLength(MinSearchLength).WithMessage($"Search text must be at least {MinSearchLength} characters.")
                .MaximumLength(MaxSearchLength).WithMessage($"Search text must not exceed {MaxSearchLength} characters.")
                .Matches(SearchPattern).WithMessage("Search text may only contain letters, numbers, spaces and . , ' & / ( ) - characters.")
                .OverridePropertyName(nameof(DoctorSearchQueryDto.Search));
        });

        When(query => !string.IsNullOrWhiteSpace(query.Specialization), () =>
        {
            RuleFor(query => query.Specialization!.Trim())
                .MaximumLength(MaxSearchLength).WithMessage($"Specialization must not exceed {MaxSearchLength} characters.")
                .Matches(SearchPattern).WithMessage("Specialization may only contain letters, numbers, spaces and . , ' & / ( ) - characters.")
                .OverridePropertyName(nameof(DoctorSearchQueryDto.Specialization));
        });

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or greater.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, DoctorSearchQueryDto.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {DoctorSearchQueryDto.MaxPageSize}.");
    }
}
