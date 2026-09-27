using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>Staff access to all medicine payments.</summary>
public interface IMedicinePaymentTransactionRepository
{
    /// <summary>
    /// Payments with their items, most recently changed first, at most <paramref name="take"/>. Null filters match all;
    /// <paramref name="search"/> matches the order number or customer name; <paramref name="orderStatus"/> the order's status;
    /// <paramref name="excludeCollected"/> leaves out orders that have been collected.
    /// </summary>
    Task<List<MedicinePayment>> ListAsync(IReadOnlyCollection<MedicinePaymentStatus>? statuses, PaymentMethod? method, string? search, int take,
        CancellationToken cancellationToken = default, string? orderStatus = null, bool excludeCollected = false);

    Task<Dictionary<MedicinePaymentStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default, bool excludeCollected = false);

    /// <summary>The payment with its items, receipts, payment and order status history (tracked), or null.</summary>
    Task<MedicinePayment?> GetDetailAsync(int paymentId, CancellationToken cancellationToken = default);

    /// <summary>The id of the payment for this pharmacy order, or null when the patient never chose a payment method.</summary>
    Task<int?> FindPaymentIdForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The outbox entries for these event ids, to show whether each order status event was sent.</summary>
    Task<List<EventOutbox>> GetOutboxEntriesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);

    void AddOutboxEntry(EventOutbox entry);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets unsaved changes after a failed save.</summary>
    void DiscardChanges();
}
