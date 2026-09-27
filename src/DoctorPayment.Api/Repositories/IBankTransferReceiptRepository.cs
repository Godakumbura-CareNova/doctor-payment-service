using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>Storage for bank transfer receipts, the staff notifications about them and their Kafka outbox events.</summary>
public interface IBankTransferReceiptRepository
{
    /// <summary>The receipt with its payment (tracked), or null.</summary>
    Task<BankTransferReceipt?> GetWithPaymentAsync(int receiptId, CancellationToken cancellationToken = default);

    /// <summary>Receipts with their payments, newest first; all statuses when <paramref name="status"/> is null.</summary>
    Task<List<BankTransferReceipt>> ListAsync(BankTransferReceiptStatus? status, CancellationToken cancellationToken = default);

    /// <summary>The outbox entries for these event ids, to show whether each verification event was sent.</summary>
    Task<List<EventOutbox>> GetOutboxEntriesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);

    /// <summary>GCN-42: which of these events have become a patient notification.</summary>
    Task<HashSet<Guid>> GetNotifiedEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);

    /// <summary>GCN-42: a verified receipt (other than <paramref name="excludingReceiptId"/>) with this transaction reference, or null.</summary>
    Task<BankTransferReceipt?> FindVerifiedByReferenceAsync(string reference, int excludingReceiptId, CancellationToken cancellationToken = default);

    Task<List<PaymentStaffNotification>> ListNotificationsAsync(int take, CancellationToken cancellationToken = default);

    Task<PaymentStaffNotification?> GetNotificationAsync(int notificationId, CancellationToken cancellationToken = default);

    void Add(BankTransferReceipt receipt);

    void AddOutboxEntry(EventOutbox entry);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets unsaved changes after a failed save, so the next attempt starts clean.</summary>
    void DiscardChanges();
}
