using DoctorPayment.Api.DTOs.FeeChangeRequest;
using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IFeeChangeRequestService
{
    /// <summary>The doctor's live fee, pending request (if any) and recent requests. Empty when there is no profile.</summary>
    Task<MyFeeChangeRequestsDto> GetMineAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a pending request. Throws <see cref="Exceptions.DoctorProfileRequiredException"/> without a profile,
    /// <see cref="ArgumentException"/> when the fee equals the current one, and
    /// <see cref="Exceptions.FeeChangeRequestConflictException"/> when a request is already pending.
    /// </summary>
    Task<FeeChangeRequestResponseDto> SubmitAsync(Guid doctorId, CreateFeeChangeRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Requests for admin review, optionally filtered by status.</summary>
    Task<List<AdminFeeChangeRequestDto>> ListAsync(FeeChangeRequestStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the requested fee to the doctor's profile and notifies them. Returns null when the request does not exist;
    /// throws <see cref="Exceptions.FeeChangeRequestConflictException"/> when it was already reviewed.
    /// </summary>
    Task<AdminFeeChangeRequestDto?> ApproveAsync(int requestId, Guid adminId, CancellationToken cancellationToken = default);

    /// <summary>Rejects the request with a reason and notifies the doctor; the fee is unchanged. Same null / conflict rules as approve.</summary>
    Task<AdminFeeChangeRequestDto?> RejectAsync(int requestId, Guid adminId, string reason, CancellationToken cancellationToken = default);
}
