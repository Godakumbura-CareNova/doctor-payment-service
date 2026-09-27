using DoctorPayment.Api.DTOs.MedicinePayment;

namespace DoctorPayment.Api.Services.Interfaces;

/// <summary>
/// Pharmacy staff and admins monitoring medicine payment transactions and order statuses. Only pharmacy staff change
/// them; admins view.
/// </summary>
public interface IMedicinePaymentTransactionService
{
    /// <summary>
    /// Transactions whose order is not yet collected, most recently changed first, with counts per category. Collected
    /// orders are listed by <see cref="ListCompletedAsync"/>. <paramref name="category"/> is Pending,
    /// Successful, Failed, Rejected or All; <paramref name="method"/> is Online, BankTransfer, Cash or empty.
    /// Throws <see cref="ArgumentException"/> for an unknown category or method.
    /// </summary>
    Task<MedicinePaymentTransactionListDto> ListAsync(string? category, string? method, string? search, CancellationToken cancellationToken = default);

    /// <summary>Completed orders (collected), most recently changed first, optionally searched by order number or customer name.</summary>
    Task<List<MedicinePaymentTransactionSummaryDto>> ListCompletedAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>
    /// The transaction with its order items, receipts and histories, or null when it does not exist. When
    /// <paramref name="canManage"/> (pharmacy staff), the order status is refreshed from the pharmacy-order service with
    /// their token (best effort) and the allowed changes are listed; admins get a read-only view.
    /// </summary>
    Task<MedicinePaymentTransactionDetailDto?> GetAsync(int paymentId, bool canManage = false, string? authorizationHeader = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the payment to "Paid" or "Failed" when its current status allows it, recording who did it and why. Null when
    /// the payment does not exist. Throws <see cref="ArgumentException"/> for an unknown status or a missing/too long note,
    /// and <see cref="Exceptions.InvalidPaymentStatusChangeException"/> for a change that is not allowed.
    /// </summary>
    Task<MedicinePaymentTransactionDetailDto?> UpdateStatusAsync(int paymentId, Guid staffId, string staffRole,
        UpdateMedicinePaymentStatusRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>The payment id for a pharmacy order, or null when it has no payment (so its status cannot be managed here).</summary>
    Task<int?> FindPaymentIdForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the order's status in the pharmacy-order service, records the change and queues a
    /// <see cref="Events.EventTypes.MedicineOrderStatusUpdated"/> Kafka event on <see cref="Events.EventTypes.OrderStatusTopic"/>,
    /// from which the patient is notified. Null when the payment does not exist. Throws <see cref="ArgumentException"/> for an
    /// unknown status or missing reason, <see cref="Exceptions.InvalidOrderStatusChangeException"/> for a change that is not
    /// allowed (nothing is changed or published), <see cref="Exceptions.OrderServiceUnavailableException"/> when the pharmacy
    /// cannot be reached, and <see cref="Exceptions.OrderStatusRecordFailedException"/> when the change could not be recorded here.
    /// </summary>
    Task<MedicinePaymentTransactionDetailDto?> UpdateOrderStatusAsync(int paymentId, Guid staffId, UpdateMedicineOrderStatusRequestDto request,
        string? authorizationHeader, CancellationToken cancellationToken = default);
}
