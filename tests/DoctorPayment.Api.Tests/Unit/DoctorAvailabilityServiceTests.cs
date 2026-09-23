using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>Runs the real repository against the in-memory provider with a pinned clock.</summary>
public sealed class DoctorAvailabilityServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tomorrow9 = Now.AddDays(1);

    [Fact]
    public async Task GetMineAsync_ReturnsEmpty_WhenNoProfile()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        Assert.Empty(await service.GetMineAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMineAsync_ListsUpcomingSlotsIncludingBooked_SoonestFirst_AndHidesEndedOnes()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
        {
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Now.AddDays(-1), EndUtc = Now.AddDays(-1).AddMinutes(20) });
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Now.AddDays(2), EndUtc = Now.AddDays(2).AddMinutes(20), IsBooked = true });
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Now.AddDays(1), EndUtc = Now.AddDays(1).AddMinutes(20) });
        });
        var service = CreateService(dbContext);

        var slots = await service.GetMineAsync(doctorId);

        Assert.Equal(new[] { Now.AddDays(1), Now.AddDays(2) }, slots.Select(slot => slot.StartUtc));
        Assert.Equal(new[] { false, true }, slots.Select(slot => slot.IsBooked));
    }

    [Fact]
    public async Task AddAsync_SplitsTheWindowIntoSlots_AndDropsTheRemainder()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);

        var created = await service.AddAsync(doctorId, new CreateAvailabilityRequestDto
        {
            StartUtc = Tomorrow9,
            EndUtc = Tomorrow9.AddMinutes(70),
            SlotDurationMinutes = 20
        });

        Assert.Equal(3, created.Count);
        Assert.Equal(new[] { Tomorrow9, Tomorrow9.AddMinutes(20), Tomorrow9.AddMinutes(40) }, created.Select(slot => slot.StartUtc));
        Assert.All(created, slot => Assert.Equal(TimeSpan.FromMinutes(20), slot.EndUtc - slot.StartUtc));
        Assert.All(created, slot => Assert.False(slot.IsBooked));
        Assert.Equal(3, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task AddAsync_TreatsUnspecifiedKindAsUtc()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);
        var unspecified = DateTime.SpecifyKind(Tomorrow9, DateTimeKind.Unspecified);

        var created = await service.AddAsync(doctorId, new CreateAvailabilityRequestDto { StartUtc = unspecified, EndUtc = unspecified.AddMinutes(30), SlotDurationMinutes = 30 });

        var slot = Assert.Single(created);
        Assert.Equal(Tomorrow9, slot.StartUtc);
        Assert.Equal(DateTimeKind.Utc, slot.StartUtc.Kind);
    }

    [Fact]
    public async Task AddAsync_RequiresAProfile()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DoctorProfileRequiredException>(() =>
            service.AddAsync(Guid.NewGuid(), new CreateAvailabilityRequestDto { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddHours(1), SlotDurationMinutes = 20 }));
    }

    [Fact]
    public async Task AddAsync_RejectsWindowsInThePast()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddAsync(doctorId, new CreateAvailabilityRequestDto { StartUtc = Now.AddMinutes(-1), EndUtc = Now.AddHours(1), SlotDurationMinutes = 20 }));

        Assert.Contains("future", exception.Message);
        Assert.Equal(0, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task AddAsync_RejectsWindowsTooFarAhead()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);
        var start = Now.AddDays(DoctorAvailabilityService.MaxDaysAhead + 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddAsync(doctorId, new CreateAvailabilityRequestDto { StartUtc = start, EndUtc = start.AddHours(1), SlotDurationMinutes = 20 }));
    }

    [Fact]
    public async Task AddAsync_RejectsOverlapWithExistingSlots_ButAllowsTouchingEdges()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(30) }));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AvailabilityConflictException>(() =>
            service.AddAsync(doctorId, new CreateAvailabilityRequestDto { StartUtc = Tomorrow9.AddMinutes(15), EndUtc = Tomorrow9.AddMinutes(45), SlotDurationMinutes = 30 }));

        var adjacent = await service.AddAsync(doctorId, new CreateAvailabilityRequestDto { StartUtc = Tomorrow9.AddMinutes(30), EndUtc = Tomorrow9.AddMinutes(60), SlotDurationMinutes = 30 });

        Assert.Single(adjacent);
        Assert.Equal(2, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task AddAsync_OverlapCheckIsPerDoctor()
    {
        await using var dbContext = CreateDbContext();
        await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(30) }));
        var otherDoctor = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);

        var created = await service.AddAsync(otherDoctor, new CreateAvailabilityRequestDto { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(30), SlotDurationMinutes = 30 });

        Assert.Single(created);
    }

    [Fact]
    public async Task AddAsync_EnforcesTheUpcomingSlotCap()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
        {
            for (var i = 0; i < DoctorAvailabilityService.MaxUpcomingSlots - 1; i++)
            {
                profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Now.AddDays(5).AddMinutes(i * 10), EndUtc = Now.AddDays(5).AddMinutes(i * 10 + 10) });
            }
        });
        var service = CreateService(dbContext);

        // Room for exactly one more; a two-slot window must be refused as a whole.
        var exception = await Assert.ThrowsAsync<AvailabilityConflictException>(() =>
            service.AddAsync(doctorId, new CreateAvailabilityRequestDto { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(40), SlotDurationMinutes = 20 }));

        Assert.Contains("at most", exception.Message);
        Assert.Equal(DoctorAvailabilityService.MaxUpcomingSlots - 1, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnOpenSlot()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(20) }));
        var slotId = await dbContext.DoctorAvailabilitySlots.Select(slot => slot.Id).SingleAsync();
        var service = CreateService(dbContext);

        Assert.True(await service.DeleteAsync(doctorId, slotId));
        Assert.Equal(0, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_RefusesBookedSlots()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(20), IsBooked = true }));
        var slotId = await dbContext.DoctorAvailabilitySlots.Select(slot => slot.Id).SingleAsync();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AvailabilityConflictException>(() => service.DeleteAsync(doctorId, slotId));
        Assert.Equal(1, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_ForUnknownSlots_OrAnotherDoctorsSlot()
    {
        await using var dbContext = CreateDbContext();
        var owner = await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(20) }));
        var intruder = await SeedProfileAsync(dbContext);
        var slotId = await dbContext.DoctorAvailabilitySlots.Select(slot => slot.Id).SingleAsync();
        var service = CreateService(dbContext);

        Assert.False(await service.DeleteAsync(intruder, slotId));
        Assert.False(await service.DeleteAsync(owner, slotId + 100));
        Assert.False(await service.DeleteAsync(Guid.NewGuid(), slotId));
        Assert.Equal(1, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    private static DoctorAvailabilityService CreateService(AppDbContext dbContext) =>
        new(new DoctorAvailabilityRepository(dbContext), new FakeTimeProvider(new DateTimeOffset(Now)));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DoctorAvailabilityService-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedProfileAsync(AppDbContext dbContext, Action<DoctorProfile>? configure = null)
    {
        var profile = new DoctorProfile
        {
            DoctorId = Guid.NewGuid(),
            FullName = "Test Doctor",
            Specialization = "General Practitioner",
            RegistrationNumber = "SLMC-1",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        configure?.Invoke(profile);
        dbContext.DoctorProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return profile.DoctorId;
    }
}
