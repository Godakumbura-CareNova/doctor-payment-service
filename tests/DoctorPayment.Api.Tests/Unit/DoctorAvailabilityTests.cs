using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using DoctorPayment.Api.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>
/// GCN-37 consultation availability. Runs the real service and repository against the in-memory
/// provider with a pinned clock.
/// </summary>
public sealed class DoctorAvailabilityTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tomorrow9 = Now.AddDays(1);

    // Scenario 1: a valid date and time is saved and becomes visible in the doctor's schedule.
    [Fact]
    public async Task AddAsync_SavesValidAvailability_AndListsIt()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);

        var created = await service.AddAsync(doctorId, new CreateAvailabilityRequestDto
        {
            StartUtc = Tomorrow9,
            EndUtc = Tomorrow9.AddMinutes(60),
            SlotDurationMinutes = 30
        });

        Assert.Equal(new[] { Tomorrow9, Tomorrow9.AddMinutes(30) }, created.Select(slot => slot.StartUtc));
        Assert.Equal(created.Select(slot => slot.Id), (await service.GetMineAsync(doctorId)).Select(slot => slot.Id));
    }

    // Scenario 2: an existing slot is updated, even when the new time overlaps its own old time.
    [Fact]
    public async Task UpdateAsync_StoresTheNewTime()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(30) }));
        var slotId = dbContext.DoctorAvailabilitySlots.Single().Id;
        var service = CreateService(dbContext);

        var updated = await service.UpdateAsync(doctorId, slotId, new UpdateAvailabilitySlotRequestDto
        {
            StartUtc = Tomorrow9.AddMinutes(15),
            EndUtc = Tomorrow9.AddMinutes(60)
        });

        Assert.NotNull(updated);
        var stored = await dbContext.DoctorAvailabilitySlots.AsNoTracking().SingleAsync();
        Assert.Equal((Tomorrow9.AddMinutes(15), Tomorrow9.AddMinutes(60)), (stored.StartUtc, stored.EndUtc));
    }

    // Scenario 3: a conflicting slot is rejected with a warning and nothing is saved.
    [Fact]
    public async Task AddAsync_RejectsAConflictingSlot()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(30) }));
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<AvailabilityConflictException>(() => service.AddAsync(doctorId, new CreateAvailabilityRequestDto
        {
            StartUtc = Tomorrow9.AddMinutes(20),
            EndUtc = Tomorrow9.AddMinutes(80),
            SlotDurationMinutes = 20
        }));

        Assert.Contains("overlaps an existing slot", exception.Message);
        Assert.Equal(1, await dbContext.DoctorAvailabilitySlots.CountAsync());
    }

    // Invalid date or time: the validator returns a message the UI can show next to the field.
    [Fact]
    public void UpdateValidator_RejectsAnEndTimeBeforeTheStart_WithAMessage()
    {
        var result = new UpdateAvailabilitySlotValidator().Validate(new UpdateAvailabilitySlotRequestDto
        {
            StartUtc = Tomorrow9,
            EndUtc = Tomorrow9.AddMinutes(-30)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "EndUtc" && error.ErrorMessage == "End time must be after the start time.");
    }

    // Unauthorized: a doctor cannot change another doctor's slot; it reads as "not found" and stays unchanged.
    [Fact]
    public async Task UpdateAsync_DeniesChangesToAnotherDoctorsSlot()
    {
        await using var dbContext = CreateDbContext();
        await SeedProfileAsync(dbContext, profile =>
            profile.AvailabilitySlots.Add(new DoctorAvailabilitySlot { StartUtc = Tomorrow9, EndUtc = Tomorrow9.AddMinutes(30) }));
        var slotId = dbContext.DoctorAvailabilitySlots.Single().Id;
        var otherDoctorId = await SeedProfileAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.UpdateAsync(otherDoctorId, slotId, new UpdateAvailabilitySlotRequestDto
        {
            StartUtc = Tomorrow9.AddHours(2),
            EndUtc = Tomorrow9.AddHours(3)
        });

        Assert.Null(result);
        Assert.Equal(Tomorrow9, (await dbContext.DoctorAvailabilitySlots.AsNoTracking().SingleAsync()).StartUtc);
    }

    private static DoctorAvailabilityService CreateService(AppDbContext dbContext) =>
        new(new DoctorAvailabilityRepository(dbContext), new FakeTimeProvider(new DateTimeOffset(Now)));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DoctorAvailability-{Guid.NewGuid():N}")
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
            RegistrationNumber = $"SLMC-{Guid.NewGuid():N}",
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
