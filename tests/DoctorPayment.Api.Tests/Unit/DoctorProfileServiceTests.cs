using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class DoctorProfileServiceTests
{
    private const string DoctorName = "Minadi Pathirana";

    [Fact]
    public async Task SaveAsync_StoresTheTokenName_ForDoctorSearch()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var saved = await service.SaveAsync(Guid.NewGuid(), "  Dr. Nimal Perera  ", ValidRequest());

        Assert.Equal("Dr. Nimal Perera", saved.FullName);
        Assert.Equal("Dr. Nimal Perera", (await dbContext.DoctorProfiles.SingleAsync()).FullName);
    }

    [Fact]
    public async Task SaveAsync_KeepsLastKnownName_WhenTokenHasNoNameClaim()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();
        await service.SaveAsync(doctorId, DoctorName, ValidRequest());

        var saved = await service.SaveAsync(doctorId, null, ValidRequest());

        Assert.Equal(DoctorName, saved.FullName);
    }

    [Fact]
    public async Task SaveAsync_StoresTheConsultationFee_RoundedToCents()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = ValidRequest();
        request.ConsultationFee = 2500.499m;

        var saved = await service.SaveAsync(Guid.NewGuid(), DoctorName, request);

        Assert.Equal(2500.50m, saved.ConsultationFee);
        Assert.Equal(2500.50m, (await dbContext.DoctorProfiles.SingleAsync()).ConsultationFee);
    }

    [Fact]
    public async Task SaveAsync_ClearsTheConsultationFee_WhenOmitted()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();
        var withFee = ValidRequest();
        withFee.ConsultationFee = 3000m;
        await service.SaveAsync(doctorId, DoctorName, withFee);

        var saved = await service.SaveAsync(doctorId, DoctorName, ValidRequest());

        Assert.Null(saved.ConsultationFee);
    }

    [Fact]
    public async Task SaveAsync_StoresOnlyThePathOfAnAbsolutePictureUrl()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = ValidRequest();
        request.ProfilePicturePath = "https://auth.example.com/uploads/profile-pictures/nimal.jpg";

        var saved = await service.SaveAsync(Guid.NewGuid(), DoctorName, request);

        Assert.Equal("/uploads/profile-pictures/nimal.jpg", saved.ProfilePicturePath);
        Assert.Equal("/uploads/profile-pictures/nimal.jpg", (await dbContext.DoctorProfiles.SingleAsync()).ProfilePicturePath);
    }

    [Fact]
    public async Task SaveAsync_ClearsThePicturePath_WhenOmitted()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();
        var withPicture = ValidRequest();
        withPicture.ProfilePicturePath = "/uploads/profile-pictures/nimal.jpg";
        await service.SaveAsync(doctorId, DoctorName, withPicture);

        var saved = await service.SaveAsync(doctorId, DoctorName, ValidRequest());

        Assert.Null(saved.ProfilePicturePath);
    }

    [Fact]
    public async Task GetByDoctorIdAsync_ReturnsNull_WhenNoProfileExists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var profile = await service.GetByDoctorIdAsync(Guid.NewGuid());

        Assert.Null(profile);
    }

    [Fact]
    public async Task SaveAsync_CreatesProfileOnFirstSave()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();

        var saved = await service.SaveAsync(doctorId, DoctorName, ValidRequest());

        Assert.Equal(doctorId, saved.DoctorId);
        Assert.Equal("General Practitioner", saved.Specialization);
        Assert.Equal("SLMC-12345", saved.RegistrationNumber);
        Assert.Single(saved.Qualifications);
        Assert.Single(saved.Experiences);
        Assert.Equal(1, await dbContext.DoctorProfiles.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_TrimsAndNormalizesText()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = ValidRequest();
        request.Specialization = "  Cardiologist  ";
        request.RegistrationNumber = " slmc-9 ";
        request.Biography = "   ";
        request.Qualifications[0].Degree = "  MBBS ";

        var saved = await service.SaveAsync(Guid.NewGuid(), DoctorName, request);

        Assert.Equal("Cardiologist", saved.Specialization);
        Assert.Equal("SLMC-9", saved.RegistrationNumber);
        Assert.Null(saved.Biography);
        Assert.Equal("MBBS", saved.Qualifications[0].Degree);
    }

    [Fact]
    public async Task SaveAsync_SecondSave_UpdatesExistingProfileAndReplacesCollections()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();
        var first = await service.SaveAsync(doctorId, DoctorName, ValidRequest());

        var update = ValidRequest();
        update.Specialization = "Paediatrician";
        update.Qualifications = new List<QualificationDto>
        {
            new() { Degree = "MD Paediatrics", Institution = "PGIM", StartYear = 2016, EndYear = 2020 },
            new() { Degree = "MBBS", Institution = "University of Peradeniya", StartYear = 2008, EndYear = 2013 }
        };
        update.Experiences.Clear();

        var second = await service.SaveAsync(doctorId, DoctorName, update);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal("Paediatrician", second.Specialization);
        Assert.Equal(new[] { "MBBS", "MD Paediatrics" }, second.Qualifications.Select(q => q.Degree));
        Assert.Empty(second.Experiences);
        Assert.Equal(1, await dbContext.DoctorProfiles.CountAsync());
        Assert.Equal(2, await dbContext.DoctorQualifications.CountAsync());
        Assert.Equal(0, await dbContext.DoctorExperiences.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_StoresOtherDetails_AndBlankDetailsBecomeNull()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = ValidRequest();
        request.OtherDetails = new List<OtherDetailDto>
        {
            new() { Title = "  Member, SLMA ", Details = "  Life member ", StartYear = 2018, EndYear = null },
            new() { Title = "Best Intern Award", Details = "   ", StartYear = 2016, EndYear = 2016 }
        };

        var saved = await service.SaveAsync(Guid.NewGuid(), DoctorName, request);

        Assert.Equal(2, saved.OtherDetails.Count);
        Assert.Equal("Best Intern Award", saved.OtherDetails[0].Title);
        Assert.Null(saved.OtherDetails[0].Details);
        Assert.Equal("Member, SLMA", saved.OtherDetails[1].Title);
        Assert.Equal("Life member", saved.OtherDetails[1].Details);
        Assert.Null(saved.OtherDetails[1].EndYear);
        Assert.Equal(2, await dbContext.DoctorOtherDetails.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_WithoutOtherDetails_IsValidAndClearsPreviousOnes()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();
        var first = ValidRequest();
        first.OtherDetails.Add(new OtherDetailDto { Title = "Member, SLMA", StartYear = 2018 });
        await service.SaveAsync(doctorId, DoctorName, first);

        var second = ValidRequest();
        second.OtherDetails.Clear();
        var saved = await service.SaveAsync(doctorId, DoctorName, second);

        Assert.Empty(saved.OtherDetails);
        Assert.Equal(0, await dbContext.DoctorOtherDetails.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyDoctorId()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Guid.Empty, DoctorName, ValidRequest()));
    }

    [Fact]
    public async Task Profiles_AreKeyedPerDoctor()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorA = Guid.NewGuid();
        var doctorB = Guid.NewGuid();
        await service.SaveAsync(doctorA, DoctorName, ValidRequest());

        Assert.NotNull(await service.GetByDoctorIdAsync(doctorA));
        Assert.Null(await service.GetByDoctorIdAsync(doctorB));
    }

    [Fact]
    public async Task DeleteAsync_RemovesProfileAndChildren()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorId = Guid.NewGuid();
        await service.SaveAsync(doctorId, DoctorName, ValidRequest());

        var deleted = await service.DeleteAsync(doctorId);

        Assert.True(deleted);
        Assert.Null(await service.GetByDoctorIdAsync(doctorId));
        Assert.Equal(0, await dbContext.DoctorProfiles.CountAsync());
        Assert.Equal(0, await dbContext.DoctorQualifications.CountAsync());
        Assert.Equal(0, await dbContext.DoctorExperiences.CountAsync());
        Assert.Equal(0, await dbContext.DoctorOtherDetails.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenNoProfileExists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        Assert.False(await service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_OnlyRemovesTheCallersProfile()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var doctorA = Guid.NewGuid();
        var doctorB = Guid.NewGuid();
        await service.SaveAsync(doctorA, DoctorName, ValidRequest());
        await service.SaveAsync(doctorB, DoctorName, ValidRequest());

        await service.DeleteAsync(doctorA);

        Assert.Null(await service.GetByDoctorIdAsync(doctorA));
        Assert.NotNull(await service.GetByDoctorIdAsync(doctorB));
    }

    private static DoctorProfileService CreateService(AppDbContext dbContext) =>
        new(new DoctorProfileRepository(dbContext));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DoctorProfileService-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static UpdateDoctorProfileRequestDto ValidRequest() => new()
    {
        Specialization = "General Practitioner",
        RegistrationNumber = "SLMC-12345",
        Biography = "Family medicine.",
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
