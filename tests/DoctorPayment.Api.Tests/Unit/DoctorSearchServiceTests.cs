using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>Runs the real repository against the in-memory provider, with the clock pinned so slot availability is deterministic.</summary>
public sealed class DoctorSearchServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SearchAsync_WithNoCriteria_ListsEveryDoctor()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto());

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.TotalPages);
    }

    [Theory]
    [InlineData("nimal", "Nimal Perera")]
    [InlineData("PERERA", "Nimal Perera")]
    [InlineData("  Kamala ", "Kamala Silva")]
    public async Task SearchAsync_MatchesName_CaseInsensitivelyAndTrimmed(string search, string expectedName)
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = search });

        var hit = Assert.Single(result.Items);
        Assert.Equal(expectedName, hit.FullName);
    }

    [Fact]
    public async Task SearchAsync_FreeText_AlsoMatchesSpecialization()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = "cardio" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal("Cardiologist", item.Specialization));
    }

    [Fact]
    public async Task SearchAsync_SpecializationFilter_IsExactAndCaseInsensitive()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Specialization = "general practitioner" });

        var hit = Assert.Single(result.Items);
        Assert.Equal("Kamala Silva", hit.FullName);
    }

    [Fact]
    public async Task SearchAsync_CombinesTextAndSpecialization()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = "Perera", Specialization = "Cardiologist" });

        var hit = Assert.Single(result.Items);
        Assert.Equal("Nimal Perera", hit.FullName);
    }

    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsEmptyResultNotError()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = "Zzzz" });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_ReportsAvailability_FromOpenUpcomingSlotsOnly()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto());
        var byName = result.Items.ToDictionary(item => item.FullName);

        // Nimal: one past slot, one booked slot, two open future slots.
        Assert.True(byName["Nimal Perera"].Availability.HasAvailableSlots);
        Assert.Equal(2, byName["Nimal Perera"].Availability.AvailableSlotCount);
        Assert.Equal(Now.AddDays(1), byName["Nimal Perera"].Availability.NextAvailableSlotUtc);

        // Kamala: only a booked slot.
        Assert.False(byName["Kamala Silva"].Availability.HasAvailableSlots);
        Assert.Equal(0, byName["Kamala Silva"].Availability.AvailableSlotCount);
        Assert.Null(byName["Kamala Silva"].Availability.NextAvailableSlotUtc);

        // Ruwan: no slots at all.
        Assert.False(byName["Ruwan Fernando"].Availability.HasAvailableSlots);
    }

    [Fact]
    public async Task SearchAsync_AvailableOnly_KeepsDoctorsWithOpenSlots()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { AvailableOnly = true });

        Assert.Equal(new[] { "Amara Jayasinghe", "Nimal Perera" }, result.Items.Select(item => item.FullName));
    }

    [Fact]
    public async Task SearchAsync_OrdersAvailableDoctorsFirst_ThenByName()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto());

        Assert.Equal(new[] { "Amara Jayasinghe", "Nimal Perera", "Kamala Silva", "Ruwan Fernando" }, result.Items.Select(item => item.FullName));
    }

    [Fact]
    public async Task SearchAsync_Pages_AndClampsPageSize()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var first = await service.SearchAsync(new DoctorSearchQueryDto { Page = 1, PageSize = 3 });
        var second = await service.SearchAsync(new DoctorSearchQueryDto { Page = 2, PageSize = 3 });
        var oversized = await service.SearchAsync(new DoctorSearchQueryDto { PageSize = 500 });

        Assert.Equal(3, first.Items.Count);
        Assert.Equal(2, first.TotalPages);
        Assert.Single(second.Items);
        Assert.Equal("Ruwan Fernando", second.Items[0].FullName);
        Assert.Equal(DoctorSearchQueryDto.MaxPageSize, oversized.PageSize);
    }

    [Fact]
    public async Task SearchAsync_SummarisesQualificationsAndExperience()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = "Nimal" });
        var nimal = result.Items.Single();

        Assert.Equal(new[] { "MBBS", "MD Cardiology" }, nimal.Qualifications);
        Assert.Equal(Now.Year - 2012, nimal.YearsOfExperience);
        Assert.Equal("SLMC-1001", nimal.RegistrationNumber);
        Assert.Equal("Heart specialist.", nimal.Biography);
        Assert.Equal(4500m, nimal.ConsultationFee);
        Assert.Equal("/uploads/profile-pictures/nimal.jpg", nimal.ProfilePicturePath);
    }

    [Fact]
    public async Task SearchAsync_LeavesConsultationFeeNull_WhenTheDoctorHasNotSetOne()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = "Kamala" });

        Assert.Null(result.Items.Single().ConsultationFee);
    }

    [Fact]
    public async Task SearchAsync_YearsOfExperienceIsNull_WhenNothingRecorded()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.SearchAsync(new DoctorSearchQueryDto { Search = "Ruwan" });

        Assert.Null(result.Items.Single().YearsOfExperience);
        Assert.Empty(result.Items.Single().Qualifications);
    }

    [Fact]
    public async Task GetPublicProfileAsync_ReturnsNull_ForUnknownOrEmptyId()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        Assert.Null(await service.GetPublicProfileAsync(Guid.NewGuid()));
        Assert.Null(await service.GetPublicProfileAsync(Guid.Empty));
    }

    [Fact]
    public async Task GetPublicProfileAsync_ReturnsPublishedDetails_WithOpenSlotsSoonestFirst()
    {
        await using var dbContext = CreateDbContext();
        var ids = await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var profile = await service.GetPublicProfileAsync(ids["Nimal Perera"]);

        Assert.NotNull(profile);
        Assert.Equal("Nimal Perera", profile.FullName);
        Assert.Equal("Cardiologist", profile.Specialization);
        Assert.Equal("SLMC-1001", profile.RegistrationNumber);
        Assert.Equal(new[] { "MBBS", "MD Cardiology" }, profile.Qualifications.Select(q => q.Degree));
        Assert.Equal(new[] { "Medical Officer", "Consultant Cardiologist" }, profile.Experiences.Select(e => e.Position));
        Assert.Single(profile.OtherDetails);
        Assert.Equal(4500m, profile.ConsultationFee);
        Assert.Equal("/uploads/profile-pictures/nimal.jpg", profile.ProfilePicturePath);
        Assert.Equal(Now.Year - 2012, profile.YearsOfExperience);
        Assert.True(profile.Availability.HasAvailableSlots);
        Assert.Equal(2, profile.Availability.AvailableSlotCount);
        Assert.Equal(new[] { Now.AddDays(1), Now.AddDays(3) }, profile.UpcomingSlots.Select(slot => slot.StartUtc));
    }

    [Fact]
    public async Task GetPublicProfileAsync_ShowsNoAvailability_WhenAllSlotsBookedOrPast()
    {
        await using var dbContext = CreateDbContext();
        var ids = await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var profile = await service.GetPublicProfileAsync(ids["Kamala Silva"]);

        Assert.NotNull(profile);
        Assert.False(profile.Availability.HasAvailableSlots);
        Assert.Empty(profile.UpcomingSlots);
    }

    [Fact]
    public async Task GetSpecializationsAsync_ReturnsDistinctSortedValues()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        var specializations = await service.GetSpecializationsAsync();

        Assert.Equal(new[] { "Cardiologist", "Dermatologist", "General Practitioner" }, specializations);
    }

    private static DoctorSearchService CreateService(AppDbContext dbContext) =>
        new(new DoctorSearchRepository(dbContext), new FakeTimeProvider(new DateTimeOffset(Now)));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DoctorSearchService-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Four doctors covering the availability states search must distinguish. Returns their ids by name.</summary>
    private static async Task<Dictionary<string, Guid>> SeedAsync(AppDbContext dbContext)
    {
        var nimal = new DoctorProfile
        {
            DoctorId = Guid.NewGuid(),
            FullName = "Nimal Perera",
            Specialization = "Cardiologist",
            RegistrationNumber = "SLMC-1001",
            Biography = "Heart specialist.",
            ConsultationFee = 4500m,
            ProfilePicturePath = "/uploads/profile-pictures/nimal.jpg",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
            Qualifications =
            {
                new DoctorQualification { Degree = "MD Cardiology", Institution = "PGIM", StartYear = 2016, EndYear = 2020 },
                new DoctorQualification { Degree = "MBBS", Institution = "University of Colombo", StartYear = 2006, EndYear = 2011 }
            },
            Experiences =
            {
                new DoctorExperience { Position = "Consultant Cardiologist", Organization = "National Hospital", StartYear = 2020, EndYear = null },
                new DoctorExperience { Position = "Medical Officer", Organization = "District General Hospital", StartYear = 2012, EndYear = 2020 }
            },
            OtherDetails = { new DoctorOtherDetail { Title = "Fellow, Sri Lanka Heart Association", StartYear = 2021 } },
            AvailabilitySlots =
            {
                new DoctorAvailabilitySlot { StartUtc = Now.AddDays(-1), EndUtc = Now.AddDays(-1).AddMinutes(20) },
                new DoctorAvailabilitySlot { StartUtc = Now.AddDays(3), EndUtc = Now.AddDays(3).AddMinutes(20) },
                new DoctorAvailabilitySlot { StartUtc = Now.AddDays(1), EndUtc = Now.AddDays(1).AddMinutes(20) },
                new DoctorAvailabilitySlot { StartUtc = Now.AddDays(2), EndUtc = Now.AddDays(2).AddMinutes(20), IsBooked = true }
            }
        };

        var kamala = new DoctorProfile
        {
            DoctorId = Guid.NewGuid(),
            FullName = "Kamala Silva",
            Specialization = "General Practitioner",
            RegistrationNumber = "SLMC-1002",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
            Qualifications = { new DoctorQualification { Degree = "MBBS", Institution = "University of Peradeniya", StartYear = 2008, EndYear = 2013 } },
            Experiences = { new DoctorExperience { Position = "General Practitioner", Organization = "Family Clinic", StartYear = 2014, EndYear = null } },
            AvailabilitySlots = { new DoctorAvailabilitySlot { StartUtc = Now.AddDays(1), EndUtc = Now.AddDays(1).AddMinutes(20), IsBooked = true } }
        };

        var ruwan = new DoctorProfile
        {
            DoctorId = Guid.NewGuid(),
            FullName = "Ruwan Fernando",
            Specialization = "Dermatologist",
            RegistrationNumber = "SLMC-1003",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        var amara = new DoctorProfile
        {
            DoctorId = Guid.NewGuid(),
            FullName = "Amara Jayasinghe",
            Specialization = "Cardiologist",
            RegistrationNumber = "SLMC-1004",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
            AvailabilitySlots = { new DoctorAvailabilitySlot { StartUtc = Now.AddHours(2), EndUtc = Now.AddHours(2).AddMinutes(20) } }
        };

        dbContext.DoctorProfiles.AddRange(nimal, kamala, ruwan, amara);
        await dbContext.SaveChangesAsync();

        return new Dictionary<string, Guid>
        {
            [nimal.FullName] = nimal.DoctorId,
            [kamala.FullName] = kamala.DoctorId,
            [ruwan.FullName] = ruwan.DoctorId,
            [amara.FullName] = amara.DoctorId
        };
    }
}
