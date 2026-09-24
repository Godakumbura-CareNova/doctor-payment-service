using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class FeeChangeRequestRepository : IFeeChangeRequestRepository
{
    /// <summary>Upper bound on the admin list so a long history cannot produce an unbounded response.</summary>
    private const int MaxListed = 200;

    private readonly AppDbContext _dbContext;

    public FeeChangeRequestRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<DoctorProfile?> GetProfileAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles.SingleOrDefaultAsync(profile => profile.DoctorId == doctorId, cancellationToken);

    public Task<List<ConsultationFeeChangeRequest>> GetRecentForProfileAsync(int profileId, int take, CancellationToken cancellationToken = default) =>
        _dbContext.ConsultationFeeChangeRequests
            .AsNoTracking()
            .Where(request => request.DoctorProfileId == profileId)
            .OrderByDescending(request => request.SubmittedAtUtc).ThenByDescending(request => request.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingAsync(int profileId, CancellationToken cancellationToken = default) =>
        _dbContext.ConsultationFeeChangeRequests
            .AnyAsync(request => request.DoctorProfileId == profileId && request.Status == FeeChangeRequestStatus.Pending, cancellationToken);

    public Task<ConsultationFeeChangeRequest?> GetForReviewAsync(int requestId, CancellationToken cancellationToken = default) =>
        _dbContext.ConsultationFeeChangeRequests
            .Include(request => request.DoctorProfile)
            .SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken);

    public Task<List<ConsultationFeeChangeRequest>> ListAsync(FeeChangeRequestStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ConsultationFeeChangeRequests.AsNoTracking().Include(request => request.DoctorProfile).AsQueryable();
        if (status is { } filter) query = query.Where(request => request.Status == filter);

        // Pending requests are a queue (oldest waiting first); reviewed ones read as history (latest first).
        query = status == FeeChangeRequestStatus.Pending
            ? query.OrderBy(request => request.SubmittedAtUtc).ThenBy(request => request.Id)
            : query.OrderByDescending(request => request.SubmittedAtUtc).ThenByDescending(request => request.Id);

        return query.Take(MaxListed).ToListAsync(cancellationToken);
    }

    public void Add(ConsultationFeeChangeRequest request) => _dbContext.ConsultationFeeChangeRequests.Add(request);

    public void AddNotification(DoctorNotification notification) => _dbContext.DoctorNotifications.Add(notification);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);
}
