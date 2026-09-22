using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Validators;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class DoctorSearchQueryValidatorTests
{
    private readonly DoctorSearchQueryValidator _validator = new();

    [Fact]
    public void EmptyCriteria_IsValid_AndListsEveryone()
    {
        var result = _validator.Validate(new DoctorSearchQueryDto());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Ni")]
    [InlineData("Nimal Perera")]
    [InlineData("Dr. O'Neil-Smith")]
    [InlineData("Ear, Nose & Throat")]
    [InlineData("Cardiology (Adult)")]
    [InlineData("  padded  ")]
    public void Search_AcceptsNamesAndSpecializations(string search)
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Search = search });

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_BlankIsTreatedAsNoCriteria(string search)
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Search = search });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Search_RejectsSingleCharacter()
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Search = "N" });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(DoctorSearchQueryDto.Search), error.PropertyName);
        Assert.Contains("at least 2 characters", error.ErrorMessage);
    }

    [Fact]
    public void Search_RejectsTextLongerThanTheLimit()
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Search = new string('a', DoctorSearchQueryValidator.MaxSearchLength + 1) });

        var error = Assert.Single(result.Errors);
        Assert.Contains("must not exceed", error.ErrorMessage);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("name; DROP TABLE")]
    [InlineData("50%")]
    [InlineData("a_b")]
    public void Search_RejectsUnsupportedCharacters(string search)
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Search = search });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DoctorSearchQueryDto.Search) && e.ErrorMessage.Contains("may only contain"));
    }

    [Fact]
    public void Specialization_RejectsUnsupportedCharacters()
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Specialization = "Cardio*" });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(DoctorSearchQueryDto.Specialization), error.PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Page_MustBeAtLeastOne(int page)
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Page = page });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(DoctorSearchQueryDto.Page), error.PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DoctorSearchQueryDto.MaxPageSize + 1)]
    public void PageSize_MustBeWithinBounds(int pageSize)
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { PageSize = pageSize });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(DoctorSearchQueryDto.PageSize), error.PropertyName);
    }

    [Fact]
    public void ReportsEveryInvalidField_AtOnce()
    {
        var result = _validator.Validate(new DoctorSearchQueryDto { Search = "x", Page = 0, PageSize = 0 });

        Assert.Equal(3, result.Errors.Count);
    }
}
