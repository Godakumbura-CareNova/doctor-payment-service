using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Models;
using Microsoft.AspNetCore.Http;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IBankTransferReceiptService
{
    /// <summary>
    /// Stores the patient's bank transfer receipt for the order and queues the verification event. The order stays
    /// Pending and the payment waits for staff. Throws <see cref="Exceptions.InvalidReceiptFileException"/> for a missing,
    /// unsupported or oversized file, <see cref="KeyNotFoundException"/> when the order is not the patient's,
    /// <see cref="Exceptions.ReceiptAssociationException"/> when the receipt cannot be tied to the order's bank transfer,
    /// <see cref="Exceptions.ReceiptReviewConflictException"/> when a receipt is already waiting or verified,
    /// <see cref="Exceptions.OrderServiceUnavailableException"/> when the order cannot be loaded, and
    /// <see cref="Exceptions.ReceiptUploadFailedException"/> when storing fails.
    /// </summary>
    Task<BankTransferReceiptUploadResultDto> UploadAsync(Guid patientId, string? customerName, int orderId, IFormFile? file,
        string? authorizationHeader, CancellationToken cancellationToken = default);

    /// <summary>Receipts for staff, newest first; all statuses when <paramref name="status"/> is null.</summary>
    Task<List<StaffBankTransferReceiptDto>> ListAsync(BankTransferReceiptStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a waiting receipt verified and the payment Paid. Null when the receipt does not exist; throws
    /// <see cref="Exceptions.ReceiptReviewConflictException"/> when it was already reviewed.
    /// </summary>
    Task<StaffBankTransferReceiptDto?> VerifyAsync(int receiptId, Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GCN-42: approves a waiting bank transfer after checking it against the order in the pharmacy (still active, same
    /// number and total) and, when staff give them, the receipt's reference, amount and date. Null when the receipt does
    /// not exist; <see cref="ArgumentException"/> for incomplete or invalid details; <see cref="Exceptions.BankTransferMismatchException"/> (after rejecting it with the
    /// reason, which notifies the patient) when the transaction does not match the order;
    /// <see cref="Exceptions.ReceiptReviewConflictException"/> when it was already reviewed; <see cref="Exceptions.OrderServiceUnavailableException"/> when the order cannot be checked.
    /// </summary>
    Task<StaffBankTransferReceiptDto?> ApproveAsync(int receiptId, Guid staffId, ApproveBankTransferDto? details, string? authorizationHeader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a waiting receipt with a reason; the patient can upload another. Same results as <see cref="VerifyAsync"/>,
    /// plus <see cref="ArgumentException"/> for a missing or too long reason.
    /// </summary>
    Task<StaffBankTransferReceiptDto?> RejectAsync(int receiptId, Guid staffId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored receipt file, or null when the receipt does not exist. Throws
    /// <see cref="Exceptions.ReceiptFileUnavailableException"/> when the receipt exists but its file is missing or unreadable.
    /// </summary>
    Task<ReceiptFileResult?> OpenFileAsync(int receiptId, CancellationToken cancellationToken = default);

    Task<List<PaymentStaffNotificationDto>> ListNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>False when the notification does not exist.</summary>
    Task<bool> MarkNotificationReadAsync(int notificationId, CancellationToken cancellationToken = default);
}
