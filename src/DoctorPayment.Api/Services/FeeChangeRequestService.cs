using System.Globalization;
using DoctorPayment.Api.DTOs.FeeChangeRequest;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Services;

public class FeeChangeRequestService : IFeeChangeRequestService
{
    /// <summary>How many past requests the doctor's fee section shows.</summary>
    public const int HistorySize = 10;

    public const string NotificationEntityType = "FeeChangeRequest";

    private const string PendingMessage =
        "You already have a fee-change request waiting for admin review. You can submit a new one after it is approved or rejected.";

    private readonly IFeeChangeRequestRepository _repository;
    private readonly TimeProvider _timeProvider;

    public FeeChangeRequestService(IFeeChangeRequestRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<MyFeeChangeRequestsDto> GetMineAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profile = await _repository.GetProfileAsync(doctorId, cancellationToken);
        if (profile is null) return new MyFeeChangeRequestsDto();

        var requests = (await _repository.GetRecentForProfileAsync(profile.Id, HistorySize, cancellationToken)).Select(ToResponse).ToList();
        return new MyFeeChangeRequestsDto
        {
            CurrentFee = profile.ConsultationFee,
            PendingRequest = requests.FirstOrDefault(request => request.Status == nameof(FeeChangeRequestStatus.Pending)),
            Requests = requests
        };
    }

    public async Task<FeeChangeRequestResponseDto> SubmitAsync(Guid doctorId, CreateFeeChangeRequestDto request, CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty) throw new ArgumentException("A doctor identity is required.", nameof(doctorId));
        if (request.RequestedFee is not { } requestedFee) throw new ArgumentException("Enter the new consultation fee.");

        var profile = await _repository.GetProfileAsync(doctorId, cancellationToken)
            ?? throw new DoctorProfileRequiredException("Create your professional profile before requesting a consultation fee.");

        if (profile.ConsultationFee == requestedFee)
        {
            throw new ArgumentException($"Your consultation fee is already {FormatFee(requestedFee)}. Enter a different amount.");
        }

        if (await _repository.HasPendingAsync(profile.Id, cancellationToken)) throw new FeeChangeRequestConflictException(PendingMessage);

        var feeChange = new ConsultationFeeChangeRequest
        {
            DoctorProfileId = profile.Id,
            PendingDoctorProfileId = profile.Id,
            CurrentFee = profile.ConsultationFee,
            RequestedFee = decimal.Round(requestedFee, 2),
            Status = FeeChangeRequestStatus.Pending,
            SubmittedAtUtc = Now()
        };
        _repository.Add(feeChange);

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another submission won the race; the unique pending index refused this one.
            throw new FeeChangeRequestConflictException(PendingMessage);
        }

        return ToResponse(feeChange);
    }

    public async Task<List<AdminFeeChangeRequestDto>> ListAsync(FeeChangeRequestStatus? status, CancellationToken cancellationToken = default) =>
        (await _repository.ListAsync(status, cancellationToken)).Select(ToAdminResponse).ToList();

    public async Task<AdminFeeChangeRequestDto?> ApproveAsync(int requestId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var feeChange = await GetPendingForReviewAsync(requestId, cancellationToken);
        if (feeChange is null) return null;

        var profile = feeChange.DoctorProfile!;
        var previousFee = profile.ConsultationFee;
        var now = Now();

        profile.ConsultationFee = feeChange.RequestedFee;
        profile.UpdatedAtUtc = now;
        MarkReviewed(feeChange, FeeChangeRequestStatus.Approved, adminId, now);

        _repository.AddNotification(new DoctorNotification
        {
            DoctorProfileId = profile.Id,
            Title = "Consultation fee change approved",
            Message = $"Your request to change your consultation fee from {FormatFee(previousFee)} to {FormatFee(feeChange.RequestedFee)} was approved. Patients now see the new fee.",
            RelatedEntityType = NotificationEntityType,
            RelatedEntityId = feeChange.Id,
            CreatedAtUtc = now
        });

        await _repository.SaveChangesAsync(cancellationToken);
        return ToAdminResponse(feeChange);
    }

    public async Task<AdminFeeChangeRequestDto?> RejectAsync(int requestId, Guid adminId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a reason for rejecting this request.");

        var feeChange = await GetPendingForReviewAsync(requestId, cancellationToken);
        if (feeChange is null) return null;

        var profile = feeChange.DoctorProfile!;
        var now = Now();
        feeChange.RejectionReason = reason.Trim();
        MarkReviewed(feeChange, FeeChangeRequestStatus.Rejected, adminId, now);

        _repository.AddNotification(new DoctorNotification
        {
            DoctorProfileId = profile.Id,
            Title = "Consultation fee change rejected",
            Message = $"Your request to change your consultation fee to {FormatFee(feeChange.RequestedFee)} was rejected. Reason: {feeChange.RejectionReason} Your fee stays {FormatFee(profile.ConsultationFee)}.",
            RelatedEntityType = NotificationEntityType,
            RelatedEntityId = feeChange.Id,
            CreatedAtUtc = now
        });

        await _repository.SaveChangesAsync(cancellationToken);
        return ToAdminResponse(feeChange);
    }

    private async Task<ConsultationFeeChangeRequest?> GetPendingForReviewAsync(int requestId, CancellationToken cancellationToken)
    {
        var feeChange = await _repository.GetForReviewAsync(requestId, cancellationToken);
        if (feeChange is null || feeChange.DoctorProfile is null) return null;
        if (feeChange.Status != FeeChangeRequestStatus.Pending)
        {
            throw new FeeChangeRequestConflictException($"This request was already {feeChange.Status.ToString().ToLowerInvariant()}.");
        }
        return feeChange;
    }

    private static void MarkReviewed(ConsultationFeeChangeRequest feeChange, FeeChangeRequestStatus status, Guid adminId, DateTime now)
    {
        feeChange.Status = status;
        feeChange.PendingDoctorProfileId = null;
        feeChange.ReviewedAtUtc = now;
        feeChange.ReviewedByAdminId = adminId;
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string FormatFee(decimal? fee) =>
        fee is { } amount ? $"LKR {amount.ToString("N2", CultureInfo.InvariantCulture)}" : "not set";

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static FeeChangeRequestResponseDto ToResponse(ConsultationFeeChangeRequest request) => Fill(new FeeChangeRequestResponseDto(), request);

    private static AdminFeeChangeRequestDto ToAdminResponse(ConsultationFeeChangeRequest request)
    {
        var response = Fill(new AdminFeeChangeRequestDto(), request);
        response.DoctorId = request.DoctorProfile?.DoctorId ?? Guid.Empty;
        response.DoctorName = request.DoctorProfile?.FullName ?? string.Empty;
        response.Specialization = request.DoctorProfile?.Specialization ?? string.Empty;
        response.DoctorCurrentFee = request.DoctorProfile?.ConsultationFee;
        return response;
    }

    private static T Fill<T>(T response, ConsultationFeeChangeRequest request) where T : FeeChangeRequestResponseDto
    {
        response.Id = request.Id;
        response.CurrentFee = request.CurrentFee;
        response.RequestedFee = request.RequestedFee;
        response.Status = request.Status.ToString();
        response.RejectionReason = request.RejectionReason;
        response.SubmittedAtUtc = AsUtc(request.SubmittedAtUtc);
        response.ReviewedAtUtc = request.ReviewedAtUtc is { } reviewed ? AsUtc(reviewed) : null;
        return response;
    }
}
