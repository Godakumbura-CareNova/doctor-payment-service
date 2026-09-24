using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.FeeChangeRequest;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using DoctorPayment.Api.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>
/// GCN-38 consultation fee-change requests. Runs the real service and repository against the in-memory
/// provider with a pinned clock.
/// </summary>
public sealed class FeeChangeRequestTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AdminId = Guid.NewGuid();

    // Scenario 1: a valid new fee creates a pending request, and the fee itself does not change yet.
    [Fact]
    public async Task SubmitAsync_CreatesAPendingRequest_WithoutChangingTheFee()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, currentFee: 2000m);
        var service = CreateService(dbContext);

        var created = await service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 2500m });

        Assert.Equal("Pending", created.Status);
        Assert.Equal((2000m, 2500m), (created.CurrentFee!.Value, created.RequestedFee));
        var mine = await service.GetMineAsync(doctorId);
        Assert.Equal(created.Id, mine.PendingRequest?.Id);
        Assert.Equal(2000m, mine.CurrentFee);
    }

    // Scenario 2: an invalid fee gets a validation message and no request is created.
    [Fact]
    public void Validator_RejectsInvalidFees_WithAMessage()
    {
        var validator = new CreateFeeChangeRequestValidator();

        Assert.Equal("Enter the new consultation fee.", validator.Validate(new CreateFeeChangeRequestDto()).Errors.Single().ErrorMessage);
        Assert.Equal("Consultation fee must be greater than 0.", validator.Validate(new CreateFeeChangeRequestDto { RequestedFee = -5m }).Errors.Single().ErrorMessage);
        Assert.Equal("Consultation fee can have at most 2 decimal places.", validator.Validate(new CreateFeeChangeRequestDto { RequestedFee = 12.345m }).Errors.Single().ErrorMessage);
        Assert.True(validator.Validate(new CreateFeeChangeRequestDto { RequestedFee = 2500.50m }).IsValid);
    }

    // Scenario 3: a doctor with a pending request cannot submit another one.
    [Fact]
    public async Task SubmitAsync_PreventsASecondPendingRequest()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, currentFee: 2000m);
        var service = CreateService(dbContext);
        await service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 2500m });

        var exception = await Assert.ThrowsAsync<FeeChangeRequestConflictException>(() =>
            service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 3000m }));

        Assert.Contains("already have a fee-change request waiting", exception.Message);
        Assert.Equal(1, await dbContext.ConsultationFeeChangeRequests.CountAsync());
    }

    // Admin approval applies the fee, closes the request and notifies the doctor.
    [Fact]
    public async Task ApproveAsync_AppliesTheFee_AndNotifiesTheDoctor()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, currentFee: 2000m);
        var service = CreateService(dbContext);
        var created = await service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 2500m });

        var approved = await service.ApproveAsync(created.Id, AdminId);

        Assert.Equal("Approved", approved!.Status);
        Assert.Equal(2500m, (await dbContext.DoctorProfiles.AsNoTracking().SingleAsync()).ConsultationFee);
        var notification = await dbContext.DoctorNotifications.AsNoTracking().SingleAsync();
        Assert.Equal("Consultation fee change approved", notification.Title);
        Assert.False(notification.IsRead);
    }

    // Admin rejection keeps the fee, stores the reason and tells the doctor why.
    [Fact]
    public async Task RejectAsync_KeepsTheFee_AndSendsTheReasonToTheDoctor()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, currentFee: 2000m);
        var service = CreateService(dbContext);
        var created = await service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 9000m });

        var rejected = await service.RejectAsync(created.Id, AdminId, "  Above the approved range for this specialization.  ");

        Assert.Equal("Rejected", rejected!.Status);
        Assert.Equal("Above the approved range for this specialization.", rejected.RejectionReason);
        Assert.Equal(2000m, (await dbContext.DoctorProfiles.AsNoTracking().SingleAsync()).ConsultationFee);
        var notification = await dbContext.DoctorNotifications.AsNoTracking().SingleAsync();
        Assert.Equal("Consultation fee change rejected", notification.Title);
        Assert.Contains("Reason: Above the approved range for this specialization.", notification.Message);
    }

    // Invalid fee: asking for the fee the doctor already has is refused with a message, and nothing is saved.
    [Fact]
    public async Task SubmitAsync_RejectsTheSameFeeAsTheCurrentOne()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, currentFee: 2000m);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 2000m }));

        Assert.Equal("Your consultation fee is already LKR 2,000.00. Enter a different amount.", exception.Message);
        Assert.Equal(0, await dbContext.ConsultationFeeChangeRequests.CountAsync());
    }

    // Incomplete rejection: the admin must give a reason the doctor can read.
    [Fact]
    public void RejectValidator_RequiresAReason_WithAMessage()
    {
        var validator = new RejectFeeChangeRequestValidator();

        Assert.Equal("Enter a reason for rejecting this request.", validator.Validate(new RejectFeeChangeRequestDto { Reason = "   " }).Errors.Single().ErrorMessage);
        Assert.Equal("The reason must be at least 5 characters.", validator.Validate(new RejectFeeChangeRequestDto { Reason = "No" }).Errors.Single().ErrorMessage);
        Assert.True(validator.Validate(new RejectFeeChangeRequestDto { Reason = "Above the approved range." }).IsValid);
    }

    // A request can only be reviewed once: rejecting an approved request is refused and the approved fee stays.
    [Fact]
    public async Task RejectAsync_RefusesARequestThatWasAlreadyApproved()
    {
        await using var dbContext = CreateDbContext();
        var doctorId = await SeedProfileAsync(dbContext, currentFee: 2000m);
        var service = CreateService(dbContext);
        var created = await service.SubmitAsync(doctorId, new CreateFeeChangeRequestDto { RequestedFee = 2500m });
        await service.ApproveAsync(created.Id, AdminId);

        var exception = await Assert.ThrowsAsync<FeeChangeRequestConflictException>(() =>
            service.RejectAsync(created.Id, AdminId, "Changed my mind about this one."));

        Assert.Equal("This request was already approved.", exception.Message);
        Assert.Equal(2500m, (await dbContext.DoctorProfiles.AsNoTracking().SingleAsync()).ConsultationFee);
        Assert.Equal(1, await dbContext.DoctorNotifications.CountAsync());
    }

    private static FeeChangeRequestService CreateService(AppDbContext dbContext) =>
        new(new FeeChangeRequestRepository(dbContext), new FakeTimeProvider(new DateTimeOffset(Now)));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"FeeChangeRequests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedProfileAsync(AppDbContext dbContext, decimal? currentFee)
    {
        var profile = new DoctorProfile
        {
            DoctorId = Guid.NewGuid(),
            FullName = "Test Doctor",
            Specialization = "General Practitioner",
            RegistrationNumber = $"SLMC-{Guid.NewGuid():N}",
            ConsultationFee = currentFee,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        dbContext.DoctorProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return profile.DoctorId;
    }
}
