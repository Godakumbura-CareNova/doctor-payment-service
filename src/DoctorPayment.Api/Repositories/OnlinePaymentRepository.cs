using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

/// <summary>GCN-41: storage for online payment attempts and their Kafka outbox events.</summary>
public interface IOnlinePaymentRepository
{
    /// <summary>The patient's payment for the order (tracked), or null.</summary>
    Task<MedicinePayment?> GetPaymentAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>The payment's attempts (tracked), oldest first.</summary>
    Task<List<OnlinePaymentAttempt>> ListAttemptsAsync(int paymentId, CancellationToken cancellationToken = default);

    /// <summary>The attempt with this gateway reference and its payment (tracked), or null.</summary>
    Task<OnlinePaymentAttempt?> GetByGatewayReferenceAsync(string gatewayReference, CancellationToken cancellationToken = default);

    void Add(OnlinePaymentAttempt attempt);

    void AddOutboxEntry(EventOutbox entry);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets unsaved changes after a failed save.</summary>
    void DiscardChanges();
}

public class OnlinePaymentRepository : IOnlinePaymentRepository
{
    private readonly AppDbContext _dbContext;

    public OnlinePaymentRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<MedicinePayment?> GetPaymentAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default) =>
        _dbContext.MedicinePayments.Include(payment => payment.Receipts)
            .SingleOrDefaultAsync(payment => payment.PatientId == patientId && payment.OrderId == orderId, cancellationToken);

    public Task<List<OnlinePaymentAttempt>> ListAttemptsAsync(int paymentId, CancellationToken cancellationToken = default) =>
        _dbContext.OnlinePaymentAttempts.Where(attempt => attempt.MedicinePaymentId == paymentId)
            .OrderBy(attempt => attempt.AttemptNumber).ToListAsync(cancellationToken);

    public Task<OnlinePaymentAttempt?> GetByGatewayReferenceAsync(string gatewayReference, CancellationToken cancellationToken = default) =>
        _dbContext.OnlinePaymentAttempts.Include(attempt => attempt.MedicinePayment)
            .SingleOrDefaultAsync(attempt => attempt.GatewayReference == gatewayReference, cancellationToken);

    public void Add(OnlinePaymentAttempt attempt) => _dbContext.OnlinePaymentAttempts.Add(attempt);

    public void AddOutboxEntry(EventOutbox entry) => _dbContext.EventOutboxEntries.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);

    public void DiscardChanges() => _dbContext.ChangeTracker.Clear();
}
