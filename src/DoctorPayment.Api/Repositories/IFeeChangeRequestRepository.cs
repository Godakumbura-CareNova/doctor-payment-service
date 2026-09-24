using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>Storage for consultation fee-change requests and the notifications their review produces.</summary>
public interface IFeeChangeRequestRepository
{
    /// <summary>The doctor's profile (tracked), or null when no profile has been created.</summary>
    Task<DoctorProfile?> GetProfileAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>The doctor's most recent requests, newest first.</summary>
    Task<List<ConsultationFeeChangeRequest>> GetRecentForProfileAsync(int profileId, int take, CancellationToken cancellationToken = default);

    Task<bool> HasPendingAsync(int profileId, CancellationToken cancellationToken = default);

    /// <summary>One request with its doctor's profile, tracked so the review can update both.</summary>
    Task<ConsultationFeeChangeRequest?> GetForReviewAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>Requests for the admin list, with their doctor's profile. Pending ones oldest first, others newest first.</summary>
    Task<List<ConsultationFeeChangeRequest>> ListAsync(FeeChangeRequestStatus? status, CancellationToken cancellationToken = default);

    void Add(ConsultationFeeChangeRequest request);

    void AddNotification(DoctorNotification notification);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
