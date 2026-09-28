using System.Globalization;
using System.Text.Json;
using DoctorPayment.Api.Clients;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Events;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Bank transfer receipts: the patient uploads one for an order paid by bank transfer, and pharmacy staff verify or
/// reject it. Uploading never confirms the order; only a staff verification marks the payment Paid. Each change is
/// saved together with its Kafka outbox event, so the event is sent even if Kafka is down at the time.
/// </summary>
public class BankTransferReceiptService : IBankTransferReceiptService
{
    public const string MissingFileMessage = "Select a receipt to upload.";
    public const string UnsupportedFormatMessage = "Unsupported file format. Only JPG, JPEG and PNG files are allowed.";
    public const string InvalidImageMessage = "This file is not a valid JPG, JPEG or PNG image. Please upload a photo or screenshot of your receipt.";
    public const string UploadFailedMessage = "We could not upload your receipt. Please try again.";
    public const string UploadedMessage =
        "Your receipt was uploaded and sent to the pharmacy for verification. Your order stays Pending and is not confirmed until the receipt is verified.";
    public const int MaxRejectionReasonLength = 500;
    public const int MaxTransactionReferenceLength = 100;
    public const string ReceiptFileUnavailableMessage =
        "The receipt file could not be opened. It may be missing or damaged. Reject the transaction so the patient can upload the receipt again.";

    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly IBankTransferReceiptRepository _receipts;
    private readonly IMedicinePaymentRepository _payments;
    private readonly IPharmacyOrderClient _orders;
    private readonly IReceiptFileStorage _storage;
    private readonly MedicinePaymentSettings _paymentSettings;
    private readonly ReceiptStorageSettings _storageSettings;
    private readonly KafkaSettings _kafkaSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BankTransferReceiptService> _logger;

    public BankTransferReceiptService(
        IBankTransferReceiptRepository receipts,
        IMedicinePaymentRepository payments,
        IPharmacyOrderClient orders,
        IReceiptFileStorage storage,
        IOptions<MedicinePaymentSettings> paymentSettings,
        IOptions<ReceiptStorageSettings> storageSettings,
        IOptions<KafkaSettings> kafkaSettings,
        TimeProvider timeProvider,
        ILogger<BankTransferReceiptService> logger)
    {
        _receipts = receipts;
        _payments = payments;
        _orders = orders;
        _storage = storage;
        _paymentSettings = paymentSettings.Value;
        _storageSettings = storageSettings.Value;
        _kafkaSettings = kafkaSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BankTransferReceiptUploadResultDto> UploadAsync(Guid patientId, string? customerName, int orderId, IFormFile? file,
        string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty) throw new ArgumentException("A patient identity is required.", nameof(patientId));
        var (extension, contentType) = await ValidateFileAsync(file, cancellationToken);

        if (orderId <= 0) throw new KeyNotFoundException("We could not find this order.");
        var order = await _orders.GetOrderAsync(orderId, authorizationHeader, cancellationToken)
            ?? throw new KeyNotFoundException("We could not find this order.");
        var payment = await _payments.GetForOrderAsync(patientId, orderId, cancellationToken);
        EnsureCanAttachReceipt(order, payment);

        string storedFileName;
        try
        {
            await using var content = file!.OpenReadStream();
            storedFileName = await _storage.SaveAsync(content, extension, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not store the bank transfer receipt for order {OrderId}", orderId);
            throw new ReceiptUploadFailedException(UploadFailedMessage, exception);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var name = string.IsNullOrWhiteSpace(customerName) ? "A patient" : customerName.Trim();
        var receipt = new BankTransferReceipt
        {
            MedicinePaymentId = payment!.Id,
            OrderId = orderId,
            PatientId = patientId,
            CustomerName = Truncate(name, 200),
            OriginalFileName = Truncate(Path.GetFileName(file!.FileName), 255),
            StoredFileName = storedFileName,
            ContentType = contentType,
            SizeBytes = file.Length,
            Status = BankTransferReceiptStatus.PendingVerification,
            PendingPaymentId = payment.Id,
            UploadedAtUtc = now
        };
        payment.Receipts.Add(receipt);
        AddHistory(payment, MedicinePaymentStatus.ReceiptUnderVerification, patientId, "Patient", "Uploaded a bank transfer receipt.", now);
        payment.Status = MedicinePaymentStatus.ReceiptUnderVerification;
        payment.UpdatedAtUtc = now;
        if (string.IsNullOrEmpty(payment.CustomerName)) payment.CustomerName = receipt.CustomerName;
        var items = (order.Items ?? new List<PharmacyOrderLine>())
            .Select(item => new BankReceiptEventItem(string.IsNullOrWhiteSpace(item.MedicineName) ? "Medicine" : item.MedicineName.Trim(), item.Quantity))
            .ToList();
        receipt.UploadEventId = AddEvent(EventTypes.BankReceiptUploaded, payment, receipt, now,
            "Bank transfer receipt uploaded for verification.", items);

        try
        {
            await _receipts.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing was recorded (receipt, payment status and event are one save), so drop the file and let the patient retry.
            _receipts.DiscardChanges();
            _storage.Delete(storedFileName);
            _logger.LogError(exception, "Could not record the bank transfer receipt for order {OrderId}", orderId);
            throw new ReceiptUploadFailedException(UploadFailedMessage, exception);
        }

        return new BankTransferReceiptUploadResultDto
        {
            Message = UploadedMessage,
            OrderStatus = order.Status ?? string.Empty,
            Payment = MedicinePaymentService.ToResponse(payment, _paymentSettings)
        };
    }

    public async Task<List<StaffBankTransferReceiptDto>> ListAsync(BankTransferReceiptStatus? status, CancellationToken cancellationToken = default)
    {
        var receipts = await _receipts.ListAsync(status, cancellationToken);
        var eventIds = receipts.Select(receipt => receipt.UploadEventId)
            .Concat(receipts.Where(receipt => receipt.ValidationEventId.HasValue).Select(receipt => receipt.ValidationEventId!.Value))
            .ToList();
        var eventsById = (await _receipts.GetOutboxEntriesAsync(eventIds, cancellationToken)).ToDictionary(entry => entry.EventId);
        var notified = await _receipts.GetNotifiedEventIdsAsync(eventIds, cancellationToken);
        return receipts.Select(receipt => ToStaffDto(receipt, eventsById.GetValueOrDefault(receipt.UploadEventId),
            receipt.ValidationEventId is { } validationId ? eventsById.GetValueOrDefault(validationId) : null,
            receipt.ValidationEventId is { } id && notified.Contains(id))).ToList();
    }

    public Task<StaffBankTransferReceiptDto?> VerifyAsync(int receiptId, Guid staffId, CancellationToken cancellationToken = default) =>
        ReviewAsync(receiptId, staffId, verified: true, reason: null, cancellationToken);

    public async Task<StaffBankTransferReceiptDto?> ApproveAsync(int receiptId, Guid staffId, ApproveBankTransferDto? details,
        string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        if (staffId == Guid.Empty) throw new ArgumentException("A staff identity is required.", nameof(staffId));
        var receiptDetails = ValidateDetails(details);

        var receipt = await _receipts.GetWithPaymentAsync(receiptId, cancellationToken);
        if (receipt?.MedicinePayment is not { } payment) return null;
        EnsurePending(receipt);

        // The transaction must match the order; every difference found is reported together.
        var mismatches = new List<string>();
        if (receiptDetails is var (reference, amount, transferDate))
        {
            // Optional details read from the receipt are checked too when staff give them.
            if (Math.Abs(amount - payment.Amount) > 0.01m)
            {
                mismatches.Add($"The transferred amount ({Lkr(amount)}) does not match the order total ({Lkr(payment.Amount)}).");
            }
            if (transferDate.Date < payment.CreatedAtUtc.Date)
            {
                mismatches.Add("The transfer was made before bank transfer was chosen for this order, so it does not belong to it.");
            }
            if (await _receipts.FindVerifiedByReferenceAsync(reference, receipt.Id, cancellationToken) is { } used)
            {
                var usedOrder = used.MedicinePayment?.OrderNumber ?? used.OrderId.ToString(CultureInfo.InvariantCulture);
                mismatches.Add($"Transaction reference {reference} was already used to pay order {usedOrder}.");
            }
            receipt.TransactionReference = reference;
            receipt.TransferredAmount = amount;
            receipt.TransferDateUtc = DateTime.SpecifyKind(transferDate.Date, DateTimeKind.Utc);
        }
        if (mismatches.Count == 0)
        {
            // The pharmacy-order service owns the order: it must still be active, and its number and total must be the
            // ones the patient paid for. A collected, rejected, removed or changed order cannot be approved.
            var order = await _orders.GetActiveOrderAsync(payment.OrderId, authorizationHeader, cancellationToken);
            if (order is null)
            {
                mismatches.Add("The order is no longer active in the pharmacy (it was collected, rejected or removed).");
            }
            else
            {
                if (!string.Equals(order.OrderNumber, payment.OrderNumber, StringComparison.Ordinal))
                {
                    mismatches.Add($"The pharmacy's order number ({order.OrderNumber}) does not match the payment ({payment.OrderNumber}).");
                }
                if (order.TotalAmount is { } total && Math.Abs(total - payment.Amount) > 0.01m)
                {
                    mismatches.Add($"The order total in the pharmacy ({Lkr(total)}) does not match the amount paid by bank transfer ({Lkr(payment.Amount)}).");
                }
                if (mismatches.Count == 0 && !string.IsNullOrEmpty(order.Status)) payment.OrderStatus = order.Status;
            }
        }

        if (mismatches.Count > 0)
        {
            // A transaction that does not match its order is rejected with the reason; the patient is notified and can
            // upload a valid receipt.
            var reason = Truncate("The transaction does not match the order. " + string.Join(" ", mismatches), MaxRejectionReasonLength);
            var rejected = await ReviewAsync(receiptId, staffId, verified: false, reason, cancellationToken);
            throw new BankTransferMismatchException("Approval was prevented and the transaction was rejected. " + reason, rejected!);
        }

        return await ReviewAsync(receiptId, staffId, verified: true, reason: null, cancellationToken);
    }

    public Task<StaffBankTransferReceiptDto?> RejectAsync(int receiptId, Guid staffId, string? reason, CancellationToken cancellationToken = default)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("Give a reason so the patient knows what to fix.");
        if (trimmed.Length > MaxRejectionReasonLength) throw new ArgumentException($"Keep the reason to {MaxRejectionReasonLength} characters or fewer.");
        return ReviewAsync(receiptId, staffId, verified: false, trimmed, cancellationToken);
    }

    public async Task<ReceiptFileResult?> OpenFileAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _receipts.GetWithPaymentAsync(receiptId, cancellationToken);
        if (receipt is null) return null;

        // GCN-42: the receipt is known but its file cannot be shown; say so instead of pretending it does not exist.
        Stream? content;
        try
        {
            content = _storage.Open(receipt.StoredFileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not open the file of bank transfer receipt {ReceiptId}", receiptId);
            throw new ReceiptFileUnavailableException(ReceiptFileUnavailableMessage, exception);
        }
        if (content is null || (content.CanSeek && content.Length == 0))
        {
            content?.Dispose();
            throw new ReceiptFileUnavailableException(ReceiptFileUnavailableMessage);
        }
        return new ReceiptFileResult(content, receipt.ContentType, receipt.OriginalFileName);
    }

    public async Task<List<PaymentStaffNotificationDto>> ListNotificationsAsync(CancellationToken cancellationToken = default) =>
        (await _receipts.ListNotificationsAsync(50, cancellationToken)).Select(notification => new PaymentStaffNotificationDto
        {
            Id = notification.Id,
            NotificationType = notification.NotificationType,
            Title = notification.Title,
            Message = notification.Message,
            OrderId = notification.OrderId,
            PaymentId = notification.PaymentId,
            IsRead = notification.IsRead,
            CreatedAtUtc = DateTime.SpecifyKind(notification.CreatedAtUtc, DateTimeKind.Utc)
        }).ToList();

    public async Task<bool> MarkNotificationReadAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _receipts.GetNotificationAsync(notificationId, cancellationToken);
        if (notification is null) return false;
        notification.IsRead = true;
        await _receipts.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The file must be there, be a JPG/JPEG/PNG by name and by content, and fit the size limit.</summary>
    private async Task<(string Extension, string ContentType)> ValidateFileAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0 || string.IsNullOrWhiteSpace(file.FileName)) throw new InvalidReceiptFileException(MissingFileMessage);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png")) throw new InvalidReceiptFileException(UnsupportedFormatMessage);

        if (file.Length > _storageSettings.MaxFileSizeBytes)
        {
            throw new InvalidReceiptFileException(
                $"The receipt is larger than {_storageSettings.MaxFileSizeMb} MB. Please upload a file of {_storageSettings.MaxFileSizeMb} MB or less.");
        }

        // A renamed file (e.g. a PDF saved as .png) is refused by its first bytes, not trusted by its name.
        var header = new byte[PngSignature.Length];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        if (extension == ".png" && StartsWith(header, read, PngSignature)) return (extension, "image/png");
        if (extension != ".png" && StartsWith(header, read, JpegSignature)) return (extension, "image/jpeg");
        throw new InvalidReceiptFileException(InvalidImageMessage);
    }

    private static bool StartsWith(byte[] header, int length, byte[] signature) =>
        header.AsSpan(0, length).StartsWith(signature);

    /// <summary>The receipt must belong to this order's bank transfer payment, waiting for a receipt, for the same total.</summary>
    private static void EnsureCanAttachReceipt(PharmacyOrderSnapshot order, MedicinePayment? payment)
    {
        switch (order.Status)
        {
            case "Rejected":
                throw new ReceiptAssociationException("This order was rejected by the pharmacy, so a receipt cannot be uploaded for it.");
            case "Collected":
                throw new ReceiptAssociationException("This order has already been collected, so a receipt cannot be uploaded for it.");
        }

        if (payment is null || payment.Method != PaymentMethod.BankTransfer)
        {
            throw new ReceiptAssociationException("Bank transfer is not the payment method for this order. Choose bank transfer before uploading a receipt.");
        }

        switch (payment.Status)
        {
            case MedicinePaymentStatus.ReceiptUnderVerification:
                throw new ReceiptReviewConflictException("A receipt for this order is already being verified. Please wait for the pharmacy to review it.");
            case MedicinePaymentStatus.Paid:
                throw new ReceiptReviewConflictException("The bank transfer for this order has already been verified.");
            case MedicinePaymentStatus.Failed:
                throw new ReceiptReviewConflictException("This payment was marked as failed by the pharmacy. Choose the payment method again before uploading a receipt.");
        }

        if (!string.Equals(order.OrderNumber, payment.OrderNumber, StringComparison.Ordinal) || Math.Abs(order.TotalAmount - payment.Amount) > 0.01m)
        {
            throw new ReceiptAssociationException(
                "This order has changed since you chose bank transfer, so the receipt cannot be matched to it. Choose the payment method again, then upload your receipt.");
        }
    }

    private async Task<StaffBankTransferReceiptDto?> ReviewAsync(int receiptId, Guid staffId, bool verified, string? reason, CancellationToken cancellationToken)
    {
        if (staffId == Guid.Empty) throw new ArgumentException("A staff identity is required.", nameof(staffId));

        var receipt = await _receipts.GetWithPaymentAsync(receiptId, cancellationToken);
        if (receipt?.MedicinePayment is not { } payment) return null;
        EnsurePending(receipt);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        receipt.Status = verified ? BankTransferReceiptStatus.Verified : BankTransferReceiptStatus.Rejected;
        receipt.PendingPaymentId = null;
        receipt.ReviewedBy = staffId;
        receipt.ReviewedAtUtc = now;
        receipt.RejectionReason = reason;
        var newStatus = verified ? MedicinePaymentStatus.Paid : MedicinePaymentStatus.ReceiptRejected;
        AddHistory(payment, newStatus, staffId, "PharmacyStaff",
            verified ? "Verified the bank transfer receipt." : $"Rejected the bank transfer receipt: {reason}", now);
        payment.Status = newStatus;
        payment.UpdatedAtUtc = now;
        AddEvent(verified ? EventTypes.BankReceiptVerified : EventTypes.BankReceiptRejected, payment, receipt, now,
            verified ? "Bank transfer receipt verified; the payment is confirmed." : $"Bank transfer receipt rejected: {reason}");
        // GCN-42: the verification result, alongside the GCN-40 event above; its consumer notifies the patient.
        receipt.ValidationEventId = AddValidationEvent(verified ? EventTypes.BankTransferApproved : EventTypes.BankTransferRejected,
            payment, receipt, staffId, now, reason,
            verified ? "Bank transfer approved; the payment is confirmed." : $"Bank transfer rejected: {reason}");

        try
        {
            await _receipts.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _receipts.DiscardChanges();
            throw new ReceiptReviewConflictException("This receipt was just changed somewhere else. Refresh and try again.");
        }

        return ToStaffDto(receipt, null, null, false);
    }

    /// <summary>
    /// GCN-42: receipt details are optional; null when none are given. When any are given, the reference, amount and
    /// date must all be there and sensible before they are compared with the order.
    /// </summary>
    private (string Reference, decimal Amount, DateTime TransferDate)? ValidateDetails(ApproveBankTransferDto? details)
    {
        if (details is null
            || (string.IsNullOrWhiteSpace(details.TransactionReference) && details.TransferredAmount is null && details.TransferDate is null))
        {
            return null;
        }

        var reference = details.TransactionReference?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(reference)) throw new ArgumentException("Enter the transaction reference shown on the receipt.");
        if (reference.Length > MaxTransactionReferenceLength)
        {
            throw new ArgumentException($"Keep the transaction reference to {MaxTransactionReferenceLength} characters or fewer.");
        }
        if (details.TransferredAmount is not > 0m) throw new ArgumentException("Enter the transferred amount shown on the receipt.");
        if (details.TransferDate is not { } transferDate) throw new ArgumentException("Enter the transfer date shown on the receipt.");
        if (transferDate.Date > _timeProvider.GetUtcNow().UtcDateTime.Date) throw new ArgumentException("The transfer date cannot be in the future.");
        return (reference, decimal.Round(details.TransferredAmount.Value, 2), transferDate);
    }

    private static void EnsurePending(BankTransferReceipt receipt)
    {
        if (receipt.Status != BankTransferReceiptStatus.PendingVerification)
        {
            throw new ReceiptReviewConflictException(receipt.Status == BankTransferReceiptStatus.Verified
                ? "This receipt has already been verified."
                : "This receipt has already been rejected.");
        }
    }

    /// <summary>GCN-42: queues a validation result event (approved or rejected) in the outbox and returns its id.</summary>
    private Guid AddValidationEvent(string eventType, MedicinePayment payment, BankTransferReceipt receipt, Guid staffId, DateTime occurredAt,
        string? reason, string message)
    {
        var eventId = Guid.NewGuid();
        var data = new BankTransferValidationEventData(receipt.Id, payment.Id, payment.OrderId, payment.OrderNumber, payment.Amount,
            payment.PatientId, receipt.CustomerName, eventType, payment.Status.ToString(), receipt.TransactionReference,
            receipt.TransferredAmount, reason, staffId, occurredAt, message);
        var validationEvent = new PaymentEvent(eventId, eventType, occurredAt, payment.OrderId, "MedicineOrder",
            JsonSerializer.SerializeToElement(data));
        _receipts.AddOutboxEntry(new EventOutbox
        {
            EventId = eventId,
            Topic = EventTypes.BankTransferValidationTopic,
            EventType = eventType,
            EventKey = payment.OrderId.ToString(CultureInfo.InvariantCulture),
            OccurredAtUtc = occurredAt,
            PayloadJson = JsonSerializer.Serialize(validationEvent)
        });
        return eventId;
    }

    private static string Lkr(decimal amount) => $"LKR {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    /// <summary>Queues a payment event in the outbox, in the same save as the change, and returns its id.</summary>
    /// <remarks>Only the upload event lists the medicines: it is the one staff act on, and the order (with its items) is read then.</remarks>
    private Guid AddEvent(string eventType, MedicinePayment payment, BankTransferReceipt receipt, DateTime occurredAt, string message,
        IReadOnlyList<BankReceiptEventItem>? items = null)
    {
        var eventId = Guid.NewGuid();
        var data = new BankReceiptEventData(payment.Id, payment.OrderId, payment.OrderNumber, payment.Amount, payment.PatientId,
            receipt.CustomerName, receipt.OriginalFileName, payment.Status.ToString(), message, items);
        var paymentEvent = new PaymentEvent(eventId, eventType, occurredAt, payment.OrderId, "MedicineOrder",
            JsonSerializer.SerializeToElement(data));
        _receipts.AddOutboxEntry(new EventOutbox
        {
            EventId = eventId,
            Topic = EventTypes.PaymentTopic,
            EventType = eventType,
            EventKey = payment.OrderId.ToString(CultureInfo.InvariantCulture),
            OccurredAtUtc = occurredAt,
            PayloadJson = JsonSerializer.Serialize(paymentEvent)
        });
        return eventId;
    }

    private StaffBankTransferReceiptDto ToStaffDto(BankTransferReceipt receipt, EventOutbox? uploadEvent, EventOutbox? validationEvent,
        bool patientNotified) => new()
    {
        Id = receipt.Id,
        PaymentId = receipt.MedicinePaymentId,
        OrderId = receipt.OrderId,
        OrderNumber = receipt.MedicinePayment?.OrderNumber ?? string.Empty,
        Amount = receipt.MedicinePayment?.Amount ?? 0m,
        CustomerName = receipt.CustomerName,
        FileName = receipt.OriginalFileName,
        ContentType = receipt.ContentType,
        SizeBytes = receipt.SizeBytes,
        Status = receipt.Status.ToString(),
        PaymentStatus = receipt.MedicinePayment?.Status.ToString() ?? string.Empty,
        UploadedAtUtc = DateTime.SpecifyKind(receipt.UploadedAtUtc, DateTimeKind.Utc),
        ReviewedAtUtc = receipt.ReviewedAtUtc is { } reviewed ? DateTime.SpecifyKind(reviewed, DateTimeKind.Utc) : null,
        RejectionReason = receipt.RejectionReason,
        VerificationEvent = EventState(uploadEvent),
        OrderStatus = receipt.MedicinePayment?.OrderStatus ?? string.Empty,
        PaymentCreatedAtUtc = DateTime.SpecifyKind(receipt.MedicinePayment?.CreatedAtUtc ?? default, DateTimeKind.Utc),
        Items = (receipt.MedicinePayment?.Items ?? new List<MedicinePaymentItem>()).OrderBy(item => item.Id)
            .Select(item => new MedicinePaymentItemDto
            {
                MedicineName = item.MedicineName, Quantity = item.Quantity, UnitPrice = item.UnitPrice, LineTotal = item.UnitPrice * item.Quantity
            }).ToList(),
        TransactionReference = receipt.TransactionReference,
        TransferredAmount = receipt.TransferredAmount,
        TransferDateUtc = receipt.TransferDateUtc is { } transferDate ? DateTime.SpecifyKind(transferDate, DateTimeKind.Utc) : null,
        ValidationEvent = receipt.ValidationEventId is null ? null : EventState(validationEvent),
        PatientNotified = patientNotified
    };

    private string EventState(EventOutbox? entry) => entry switch
    {
        null => "Queued",
        { PublishedAtUtc: not null } => "Published",
        { PublishAttempts: var attempts } when attempts >= _kafkaSettings.PublishRetryLimit => "Failed",
        _ => "Queued"
    };

    private static void AddHistory(MedicinePayment payment, MedicinePaymentStatus toStatus, Guid changedBy, string role, string note, DateTime now) =>
        payment.StatusHistory.Add(new MedicinePaymentStatusChange
        {
            FromStatus = payment.Status,
            ToStatus = toStatus,
            ChangedBy = changedBy,
            ChangedByRole = role,
            Note = Truncate(note, 500),
            ChangedAtUtc = now
        });

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
