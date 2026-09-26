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
        payment.Status = MedicinePaymentStatus.ReceiptUnderVerification;
        payment.UpdatedAtUtc = now;
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
        var events = await _receipts.GetOutboxEntriesAsync(receipts.Select(receipt => receipt.UploadEventId).ToList(), cancellationToken);
        var eventsById = events.ToDictionary(entry => entry.EventId);
        return receipts.Select(receipt => ToStaffDto(receipt, eventsById.GetValueOrDefault(receipt.UploadEventId))).ToList();
    }

    public Task<StaffBankTransferReceiptDto?> VerifyAsync(int receiptId, Guid staffId, CancellationToken cancellationToken = default) =>
        ReviewAsync(receiptId, staffId, verified: true, reason: null, cancellationToken);

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
        var content = _storage.Open(receipt.StoredFileName);
        return content is null ? null : new ReceiptFileResult(content, receipt.ContentType, receipt.OriginalFileName);
    }

    public async Task<List<PaymentStaffNotificationDto>> ListNotificationsAsync(CancellationToken cancellationToken = default) =>
        (await _receipts.ListNotificationsAsync(50, cancellationToken)).Select(notification => new PaymentStaffNotificationDto
        {
            Id = notification.Id,
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
        if (receipt.Status != BankTransferReceiptStatus.PendingVerification)
        {
            throw new ReceiptReviewConflictException(receipt.Status == BankTransferReceiptStatus.Verified
                ? "This receipt has already been verified."
                : "This receipt has already been rejected.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        receipt.Status = verified ? BankTransferReceiptStatus.Verified : BankTransferReceiptStatus.Rejected;
        receipt.PendingPaymentId = null;
        receipt.ReviewedBy = staffId;
        receipt.ReviewedAtUtc = now;
        receipt.RejectionReason = reason;
        payment.Status = verified ? MedicinePaymentStatus.Paid : MedicinePaymentStatus.ReceiptRejected;
        payment.UpdatedAtUtc = now;
        AddEvent(verified ? EventTypes.BankReceiptVerified : EventTypes.BankReceiptRejected, payment, receipt, now,
            verified ? "Bank transfer receipt verified; the payment is confirmed." : $"Bank transfer receipt rejected: {reason}");

        try
        {
            await _receipts.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _receipts.DiscardChanges();
            throw new ReceiptReviewConflictException("This receipt was just changed somewhere else. Refresh and try again.");
        }

        return ToStaffDto(receipt, null);
    }

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

    private StaffBankTransferReceiptDto ToStaffDto(BankTransferReceipt receipt, EventOutbox? uploadEvent) => new()
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
        VerificationEvent = uploadEvent switch
        {
            null => "Queued",
            { PublishedAtUtc: not null } => "Published",
            { PublishAttempts: var attempts } when attempts >= _kafkaSettings.PublishRetryLimit => "Failed",
            _ => "Queued"
        }
    };

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
