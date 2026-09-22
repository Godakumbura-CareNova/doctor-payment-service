using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.Validators;
using FluentValidation.TestHelper;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class UpdateDoctorProfileValidatorTests
{
    private readonly UpdateDoctorProfileValidator _validator = new();

    [Fact]
    public void ValidProfile_PassesValidation()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ProfileWithoutQualificationsOrExperience_IsStillValid()
    {
        var request = ValidRequest();
        request.Qualifications.Clear();
        request.Experiences.Clear();

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void MissingOrShortSpecialization_Fails(string specialization)
    {
        var request = ValidRequest();
        request.Specialization = specialization;

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Specialization);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("SLMC 123")]
    [InlineData("SLMC#123")]
    public void InvalidRegistrationNumber_Fails(string registrationNumber)
    {
        var request = ValidRequest();
        request.RegistrationNumber = registrationNumber;

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.RegistrationNumber);
    }

    [Fact]
    public void BiographyOverLimit_Fails()
    {
        var request = ValidRequest();
        request.Biography = new string('x', 2001);

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Biography);
    }

    [Fact]
    public void QualificationWithMissingDegree_FailsOnIndexedProperty()
    {
        var request = ValidRequest();
        request.Qualifications[0].Degree = "";

        _validator.TestValidate(request).ShouldHaveValidationErrorFor("Qualifications[0].Degree");
    }

    [Fact]
    public void QualificationStartYearBeforeMinimum_Fails()
    {
        var request = ValidRequest();
        request.Qualifications[0].StartYear = 1900;

        _validator.TestValidate(request).ShouldHaveValidationErrorFor("Qualifications[0].StartYear");
    }

    [Fact]
    public void QualificationStartYearInFuture_Fails()
    {
        var request = ValidRequest();
        request.Qualifications[0].StartYear = DateTime.UtcNow.Year + 1;

        _validator.TestValidate(request).ShouldHaveValidationErrorFor("Qualifications[0].StartYear");
    }

    [Fact]
    public void ExperienceEndYearBeforeStartYear_Fails()
    {
        var request = ValidRequest();
        request.Experiences[0].StartYear = 2018;
        request.Experiences[0].EndYear = 2016;

        _validator.TestValidate(request).ShouldHaveValidationErrorFor("Experiences[0].EndYear");
    }

    [Fact]
    public void ExperienceWithNullEndYear_IsTreatedAsCurrentPosition()
    {
        var request = ValidRequest();
        request.Experiences[0].EndYear = null;

        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor("Experiences[0].EndYear");
    }

    [Fact]
    public void ExperienceWithMissingOrganization_Fails()
    {
        var request = ValidRequest();
        request.Experiences[0].Organization = " ";

        _validator.TestValidate(request).ShouldHaveValidationErrorFor("Experiences[0].Organization");
    }

    [Fact]
    public void OtherDetailsSection_IsOptional()
    {
        var request = ValidRequest();
        request.OtherDetails.Clear();

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OtherDetailWithoutTitle_Fails()
    {
        var request = ValidRequest();
        request.OtherDetails[0].Title = " ";

        _validator.TestValidate(request).ShouldHaveValidationErrorFor("OtherDetails[0].Title");
    }

    [Fact]
    public void OtherDetailDetails_IsOptionalButLimited()
    {
        var request = ValidRequest();
        request.OtherDetails[0].Details = null;
        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor("OtherDetails[0].Details");

        request.OtherDetails[0].Details = new string('x', 301);
        _validator.TestValidate(request).ShouldHaveValidationErrorFor("OtherDetails[0].Details");
    }

    [Fact]
    public void OtherDetailEndYear_NullMeansPresent_ButCannotPrecedeStart()
    {
        var request = ValidRequest();
        request.OtherDetails[0].EndYear = null;
        _validator.TestValidate(request).ShouldNotHaveValidationErrorFor("OtherDetails[0].EndYear");

        request.OtherDetails[0].StartYear = 2019;
        request.OtherDetails[0].EndYear = 2018;
        _validator.TestValidate(request).ShouldHaveValidationErrorFor("OtherDetails[0].EndYear");
    }

    [Fact]
    public void TooManyQualifications_Fails()
    {
        var request = ValidRequest();
        request.Qualifications = Enumerable.Range(0, UpdateDoctorProfileValidator.MaxEntries + 1)
            .Select(_ => new QualificationDto { Degree = "MBBS", Institution = "University", StartYear = 2000, EndYear = 2005 })
            .ToList();

        _validator.TestValidate(request).ShouldHaveValidationErrorFor(r => r.Qualifications);
    }

    private static UpdateDoctorProfileRequestDto ValidRequest() => new()
    {
        Specialization = "General Practitioner",
        RegistrationNumber = "SLMC-12345",
        Biography = "Bio",
        Qualifications = new List<QualificationDto>
        {
            new() { Degree = "MBBS", Institution = "University of Colombo", StartYear = 2010, EndYear = 2015 }
        },
        Experiences = new List<ExperienceDto>
        {
            new() { Position = "Senior Medical Officer", Organization = "District General Hospital, Colombo", StartYear = 2016, EndYear = 2020 }
        },
        OtherDetails = new List<OtherDetailDto>
        {
            new() { Title = "Member, Sri Lanka Medical Association", Details = "Life member", StartYear = 2018, EndYear = null }
        }
    };
}
