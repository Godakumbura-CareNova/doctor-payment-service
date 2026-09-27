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
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Staff view of medicine payments and their orders. Staff may only record the outcome of payments that happen outside
/// this system (online and cash) or give up on unpaid ones; a bank transfer is confirmed only through receipt
/// verification. Order status changes are applied by the pharmacy-order service, then recorded here with a Kafka event
/// from which the patient is notified.
/// </summary>
public class MedicinePaymentTransactionService : IMedicinePaymentTransactionService
{
    public const int MaxListSize = 200;
    public const int MaxNoteLength = 500;
    public const int MaxRejectionReasonLength = 1000;

    public const string Pending = "Pending";
    public const string Successful = "Successful";
    public const string Failed = "Failed";
    public const string Rejected = "Rejected";

    /// <summary>The statuses staff may set from each payment status. Anything not listed cannot be changed here.</summary>
    private static readonly Dictionary<MedicinePaymentStatus, MedicinePaymentStatus[]> AllowedChanges = new()
    {
        [MedicinePaymentStatus.AwaitingOnlinePayment] = new[] { MedicinePaymentStatus.Paid, MedicinePaymentStatus.Failed },
        [MedicinePaymentStatus.PayOnCollection] = new[] { MedicinePaymentStatus.Paid, MedicinePaymentStatus.Failed },
        [MedicinePaymentStatus.AwaitingBankTransfer] = new[] { MedicinePaymentStatus.Failed },
        [MedicinePaymentStatus.ReceiptRejected] = new[] { MedicinePaymentStatus.Failed }
    };

    /// <summary>The order statuses staff may set from each order status (the pharmacy-order service's rules).</summary>
    private static readonly Dictionary<string, string[]> AllowedOrderChanges = new()
    {
        [MedicineOrderStatuses.Pending] = new[] { MedicineOrderStatuses.ReadyForCollection, MedicineOrderStatuses.Rejected },
        [MedicineOrderStatuses.ReadyForCollection] = new[] { MedicineOrderStatuses.Collected }
    };

    private static readonly Dictionary<string, string> OrderStatusLabels = new()
    {
        [MedicineOrderStatuses.Pending] = "pending",
        [MedicineOrderStatuses.ReadyForCollection] = "ready for collection",
        [MedicineOrderStatuses.Collected] = "collected",
        [MedicineOrderStatuses.Rejected] = "rejected"
    };

    private readonly IMedicinePaymentTransactionRepository _repository;
    private readonly IPharmacyOrderClient _orders;
    private readonly KafkaSettings _kafkaSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MedicinePaymentTransactionService> _logger;

    public MedicinePaymentTransactionService(
        IMedicinePaymentTransactionRepository repository,
        IPharmacyOrderClient orders,
        IOptions<KafkaSettings> kafkaSettings,
        TimeProvider timeProvider,
        ILogger<MedicinePaymentTransactionService> logger)
    {
        _repository = repository;
        _orders = orders;
        _kafkaSettings = kafkaSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Which of the four groups staff filter by a payment status belongs to.</summary>
    public static string CategoryOf(MedicinePaymentStatus status) => status switch
    {
        MedicinePaymentStatus.Paid => Successful,
        MedicinePaymentStatus.Failed => Failed,
        MedicinePaymentStatus.ReceiptRejected => Rejected,
        _ => Pending
    };

    public static string OrderStatusLabel(string status) => OrderStatusLabels.GetValueOrDefault(status, status);

    public async Task<MedicinePaymentTransactionListDto> ListAsync(string? category, string? method, string? search, CancellationToken cancellationToken = default)
    {
        var statuses = StatusesFor(category);
        PaymentMethod? methodFilter = null;
        if (!string.IsNullOrWhiteSpace(method))
        {
            if (!MedicinePaymentService.TryParseMethod(method, out var parsed)) throw new ArgumentException("Method must be Online, BankTransfer or Cash.");
            methodFilter = parsed;
        }

        // Collected orders are finished: they leave this list (and its counts) and appear under completed orders instead.
        var payments = await _repository.ListAsync(statuses, methodFilter, search, MaxListSize, cancellationToken, excludeCollected: true);
        var counts = await _repository.CountByStatusAsync(cancellationToken, excludeCollected: true);
        int CountOf(string group) => counts.Where(pair => CategoryOf(pair.Key) == group).Sum(pair => pair.Value);

        return new MedicinePaymentTransactionListDto
        {
            Transactions = payments.Select(payment => Fill(new MedicinePaymentTransactionSummaryDto(), payment)).ToList(),
            Counts = new MedicinePaymentTransactionCountsDto
            {
                All = counts.Values.Sum(),
                Pending = CountOf(Pending),
                Successful = CountOf(Successful),
                Failed = CountOf(Failed),
                Rejected = CountOf(Rejected)
            }
        };
    }

    public async Task<List<MedicinePaymentTransactionSummaryDto>> ListCompletedAsync(string? search, CancellationToken cancellationToken = default) =>
        (await _repository.ListAsync(null, null, search, MaxListSize, cancellationToken, MedicineOrderStatuses.Collected))
            .Select(payment => Fill(new MedicinePaymentTransactionSummaryDto(), payment)).ToList();

    public async Task<MedicinePaymentTransactionDetailDto?> GetAsync(int paymentId, bool canManage = false, string? authorizationHeader = null,
        CancellationToken cancellationToken = default)
    {
        var payment = paymentId > 0 ? await _repository.GetDetailAsync(paymentId, cancellationToken) : null;
        if (payment is null) return null;

        if (canManage && IsActive(payment.OrderStatus))
        {
            // Best effort: staff may also change orders on the pharmacy's own page, so pick up the live status.
            try
            {
                var live = await _orders.GetActiveOrderStatusAsync(payment.OrderId, authorizationHeader, cancellationToken);
                if (live is not null && live != payment.OrderStatus)
                {
                    payment.OrderStatus = live;
                    await _repository.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception exception) when (exception is OrderServiceUnavailableException or OrderStatusChangeRefusedException)
            {
                _logger.LogInformation(exception, "Showing the last known status of order {OrderId}", payment.OrderId);
            }
        }

        return await ToDetailAsync(payment, canManage, cancellationToken);
    }

    public async Task<MedicinePaymentTransactionDetailDto?> UpdateStatusAsync(int paymentId, Guid staffId, string staffRole,
        UpdateMedicinePaymentStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        if (staffId == Guid.Empty) throw new ArgumentException("A staff identity is required.", nameof(staffId));
        var target = ParseTargetStatus(request.Status);
        var note = request.Note?.Trim();
        if (note?.Length > MaxNoteLength) throw new ArgumentException($"Keep the note to {MaxNoteLength} characters or fewer.");
        if (target == MedicinePaymentStatus.Failed && string.IsNullOrEmpty(note))
        {
            throw new ArgumentException("Give a reason for marking the payment as failed.");
        }

        var payment = paymentId > 0 ? await _repository.GetDetailAsync(paymentId, cancellationToken) : null;
        if (payment is null) return null;
        EnsureAllowed(payment, target);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        payment.StatusHistory.Add(new MedicinePaymentStatusChange
        {
            FromStatus = payment.Status,
            ToStatus = target,
            ChangedBy = staffId,
            ChangedByRole = staffRole,
            Note = string.IsNullOrEmpty(note) ? null : note,
            ChangedAtUtc = now
        });
        payment.Status = target;
        payment.UpdatedAtUtc = now;
        await _repository.SaveChangesAsync(cancellationToken);

        return await ToDetailAsync(payment, canManage: true, cancellationToken);
    }

    public Task<int?> FindPaymentIdForOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        orderId > 0 ? _repository.FindPaymentIdForOrderAsync(orderId, cancellationToken) : Task.FromResult<int?>(null);

    public async Task<MedicinePaymentTransactionDetailDto?> UpdateOrderStatusAsync(int paymentId, Guid staffId, UpdateMedicineOrderStatusRequestDto request,
        string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        if (staffId == Guid.Empty) throw new ArgumentException("A staff identity is required.", nameof(staffId));
        var target = ParseOrderStatus(request.Status);
        var reason = request.Reason?.Trim();
        if (target == MedicineOrderStatuses.Rejected && string.IsNullOrEmpty(reason)) throw new ArgumentException("Give a reason for rejecting the order.");
        if (reason?.Length > MaxRejectionReasonLength) throw new ArgumentException($"Keep the reason to {MaxRejectionReasonLength} characters or fewer.");

        var payment = paymentId > 0 ? await _repository.GetDetailAsync(paymentId, cancellationToken) : null;
        if (payment is null) return null;

        // Check against the pharmacy's live status, so a change made elsewhere is not overwritten or repeated.
        var current = payment.OrderStatus;
        if (IsActive(current))
        {
            current = await _orders.GetActiveOrderStatusAsync(payment.OrderId, authorizationHeader, cancellationToken)
                ?? throw new InvalidOrderStatusChangeException(
                    "This order is no longer active in the pharmacy (it may already be collected or rejected). Refresh the page.");
        }
        if (OrderChangeProblem(payment, current, target) is { } problem) throw new InvalidOrderStatusChangeException(problem);

        string applied;
        try
        {
            applied = await _orders.UpdateOrderStatusAsync(payment.OrderId, target, target == MedicineOrderStatuses.Rejected ? reason : null,
                authorizationHeader, cancellationToken);
        }
        catch (OrderStatusChangeRefusedException exception)
        {
            throw new InvalidOrderStatusChangeException(exception.Message);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var change = new MedicineOrderStatusChange
        {
            OrderId = payment.OrderId,
            PatientId = payment.PatientId,
            CustomerName = CustomerNameOf(payment),
            FromStatus = current,
            ToStatus = applied,
            Reason = target == MedicineOrderStatuses.Rejected ? reason : null,
            ChangedBy = staffId,
            ChangedAtUtc = now,
            EventId = Guid.NewGuid(),
            NotificationStatus = PatientNotificationStatus.Pending
        };
        payment.OrderStatusHistory.Add(change);
        payment.OrderStatus = applied;
        payment.UpdatedAtUtc = now;
        AddOrderStatusEvent(payment, change);

        try
        {
            // The change and its Kafka event are one save: an event is never queued for an unrecorded change, or the reverse.
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _repository.DiscardChanges();
            _logger.LogError(exception, "Order {OrderId} became {Status} in the pharmacy but the change could not be recorded", payment.OrderId, applied);
            throw new OrderStatusRecordFailedException(
                $"The order is now {OrderStatusLabel(applied)} in the pharmacy, but the change could not be recorded and the patient was not notified. Refresh and contact support.",
                exception);
        }

        return await ToDetailAsync(payment, canManage: true, cancellationToken);
    }

    /// <summary>Only "Paid" and "Failed" can be set by staff; the other statuses follow from the patient's actions.</summary>
    private static MedicinePaymentStatus ParseTargetStatus(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Select the new payment status.");
        if (string.Equals(name, nameof(MedicinePaymentStatus.Paid), StringComparison.OrdinalIgnoreCase)) return MedicinePaymentStatus.Paid;
        if (string.Equals(name, nameof(MedicinePaymentStatus.Failed), StringComparison.OrdinalIgnoreCase)) return MedicinePaymentStatus.Failed;
        throw new ArgumentException("Staff can only mark a payment as Paid or Failed.");
    }

    private static void EnsureAllowed(MedicinePayment payment, MedicinePaymentStatus target)
    {
        if (payment.Status == target) throw new InvalidPaymentStatusChangeException($"This payment is already marked as {target}.");
        if (AllowedChanges.TryGetValue(payment.Status, out var allowed) && allowed.Contains(target)) return;

        throw new InvalidPaymentStatusChangeException(payment.Status switch
        {
            MedicinePaymentStatus.Paid => "This payment is already confirmed as paid and cannot be changed.",
            MedicinePaymentStatus.Failed => "This payment was marked as failed. The patient has to choose a payment method again.",
            MedicinePaymentStatus.ReceiptUnderVerification =>
                "A bank transfer receipt is waiting for verification. Verify or reject the receipt instead.",
            _ when payment.Method == PaymentMethod.BankTransfer && target == MedicinePaymentStatus.Paid =>
                "A bank transfer can only be marked as paid by verifying its receipt.",
            _ => $"A payment cannot be changed from {payment.Status} to {target}."
        });
    }

    private static bool IsActive(string orderStatus) =>
        orderStatus is MedicineOrderStatuses.Pending or MedicineOrderStatuses.ReadyForCollection;

    /// <summary>"ReadyForCollection", "Rejected" or "Collected" (spaces and case ignored).</summary>
    private static string ParseOrderStatus(string? value)
    {
        var name = value?.Replace(" ", string.Empty).Trim();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Select the new order status.");
        return new[] { MedicineOrderStatuses.ReadyForCollection, MedicineOrderStatuses.Rejected, MedicineOrderStatuses.Collected }
            .FirstOrDefault(status => string.Equals(status, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose Ready for Collection, Rejected or Collected.");
    }

    /// <summary>Why the order cannot move from <paramref name="current"/> to <paramref name="target"/>; null when it can.</summary>
    private static string? OrderChangeProblem(MedicinePayment payment, string current, string target)
    {
        if (current == target) return $"This order is already {OrderStatusLabel(target)}.";
        if (current is MedicineOrderStatuses.Collected or MedicineOrderStatuses.Rejected)
        {
            return $"This order is already {OrderStatusLabel(current)} and cannot be changed.";
        }
        if (!AllowedOrderChanges.TryGetValue(current, out var allowed) || !allowed.Contains(target))
        {
            return target == MedicineOrderStatuses.Collected
                ? "An order must be ready for collection before it can be marked as collected."
                : $"An order that is {OrderStatusLabel(current)} cannot be marked as {OrderStatusLabel(target)}.";
        }

        // A paid-for order is released only once its payment is confirmed; cash is paid at the counter, before collection.
        if (target == MedicineOrderStatuses.ReadyForCollection && payment.Method != PaymentMethod.Cash && payment.Status != MedicinePaymentStatus.Paid)
        {
            return payment.Method == PaymentMethod.BankTransfer
                ? "The bank transfer for this order must be verified before the order is ready for collection."
                : "The online payment for this order must be confirmed as paid before the order is ready for collection.";
        }
        if (target == MedicineOrderStatuses.Collected && payment.Status != MedicinePaymentStatus.Paid)
        {
            return "Mark the payment as paid before marking the order as collected.";
        }

        return null;
    }

    private void AddOrderStatusEvent(MedicinePayment payment, MedicineOrderStatusChange change)
    {
        var message = change.ToStatus switch
        {
            MedicineOrderStatuses.ReadyForCollection => $"Order {payment.OrderNumber} is ready for collection.",
            MedicineOrderStatuses.Collected => $"Order {payment.OrderNumber} was collected.",
            MedicineOrderStatuses.Rejected => $"Order {payment.OrderNumber} was rejected: {change.Reason}",
            _ => $"Order {payment.OrderNumber} is now {OrderStatusLabel(change.ToStatus)}."
        };
        var data = new OrderStatusEventData(payment.OrderId, payment.OrderNumber, payment.Id, payment.PatientId, change.CustomerName,
            change.FromStatus, change.ToStatus, change.Reason, change.ChangedAtUtc, message);
        var orderEvent = new PaymentEvent(change.EventId, EventTypes.MedicineOrderStatusUpdated, change.ChangedAtUtc, payment.OrderId,
            "MedicineOrder", JsonSerializer.SerializeToElement(data));
        _repository.AddOutboxEntry(new EventOutbox
        {
            EventId = change.EventId,
            Topic = EventTypes.OrderStatusTopic,
            EventType = EventTypes.MedicineOrderStatusUpdated,
            EventKey = payment.OrderId.ToString(CultureInfo.InvariantCulture),
            OccurredAtUtc = change.ChangedAtUtc,
            PayloadJson = JsonSerializer.Serialize(orderEvent)
        });
    }

    /// <summary>Payments recorded before the name was kept on the payment still have it on their receipts.</summary>
    private static string CustomerNameOf(MedicinePayment payment) =>
        !string.IsNullOrEmpty(payment.CustomerName)
            ? payment.CustomerName
            : payment.Receipts.Select(receipt => receipt.CustomerName).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyCollection<MedicinePaymentStatus>? StatusesFor(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || string.Equals(category.Trim(), "All", StringComparison.OrdinalIgnoreCase)) return null;

        var group = new[] { Pending, Successful, Failed, Rejected }
            .FirstOrDefault(name => string.Equals(name, category.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Status must be Pending, Successful, Failed, Rejected or All.");
        return Enum.GetValues<MedicinePaymentStatus>().Where(status => CategoryOf(status) == group).ToList();
    }

    private static T Fill<T>(T dto, MedicinePayment payment) where T : MedicinePaymentTransactionSummaryDto
    {
        dto.Id = payment.Id;
        dto.OrderId = payment.OrderId;
        dto.OrderNumber = payment.OrderNumber;
        dto.CustomerName = payment.CustomerName;
        dto.Amount = payment.Amount;
        dto.Method = payment.Method.ToString();
        dto.Status = payment.Status.ToString();
        dto.Category = CategoryOf(payment.Status);
        dto.ItemCount = payment.Items.Sum(item => item.Quantity);
        dto.OrderStatus = payment.OrderStatus;
        dto.CreatedAtUtc = DateTime.SpecifyKind(payment.CreatedAtUtc, DateTimeKind.Utc);
        dto.UpdatedAtUtc = DateTime.SpecifyKind(payment.UpdatedAtUtc, DateTimeKind.Utc);
        return dto;
    }

    private async Task<MedicinePaymentTransactionDetailDto> ToDetailAsync(MedicinePayment payment, bool canManage, CancellationToken cancellationToken)
    {
        var detail = Fill(new MedicinePaymentTransactionDetailDto(), payment);
        detail.PatientId = payment.PatientId;
        detail.CanManage = canManage;
        detail.CustomerName = CustomerNameOf(payment);
        detail.Items = payment.Items.OrderBy(item => item.Id).Select(item => new MedicinePaymentItemDto
        {
            MedicineName = item.MedicineName,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            LineTotal = item.Quantity * item.UnitPrice
        }).ToList();
        detail.Receipts = payment.Receipts.OrderByDescending(receipt => receipt.UploadedAtUtc).ThenByDescending(receipt => receipt.Id)
            .Select(MedicinePaymentService.ToReceiptDto).ToList();
        detail.History = payment.StatusHistory.OrderBy(change => change.ChangedAtUtc).ThenBy(change => change.Id)
            .Select(change => new MedicinePaymentStatusChangeDto
            {
                FromStatus = change.FromStatus?.ToString(),
                ToStatus = change.ToStatus.ToString(),
                ChangedByRole = change.ChangedByRole,
                Note = change.Note,
                ChangedAtUtc = DateTime.SpecifyKind(change.ChangedAtUtc, DateTimeKind.Utc)
            }).ToList();
        detail.AllowedStatusChanges = canManage && AllowedChanges.TryGetValue(payment.Status, out var allowed)
            ? allowed.Select(status => status.ToString()).ToList()
            : new List<string>();
        detail.AllowedOrderStatusChanges = canManage && AllowedOrderChanges.TryGetValue(payment.OrderStatus, out var orderTargets)
            ? orderTargets.Where(target => OrderChangeProblem(payment, payment.OrderStatus, target) is null).ToList()
            : new List<string>();

        var changes = payment.OrderStatusHistory.OrderBy(change => change.ChangedAtUtc).ThenBy(change => change.Id).ToList();
        var events = changes.Count == 0
            ? new Dictionary<Guid, EventOutbox>()
            : (await _repository.GetOutboxEntriesAsync(changes.Select(change => change.EventId).ToList(), cancellationToken))
                .ToDictionary(entry => entry.EventId);
        detail.OrderStatusHistory = changes.Select(change => new MedicineOrderStatusChangeDto
        {
            FromStatus = change.FromStatus,
            ToStatus = change.ToStatus,
            Reason = change.Reason,
            ChangedAtUtc = DateTime.SpecifyKind(change.ChangedAtUtc, DateTimeKind.Utc),
            EventStatus = events.GetValueOrDefault(change.EventId) switch
            {
                { PublishedAtUtc: not null } => "Published",
                { PublishAttempts: var attempts } when attempts >= _kafkaSettings.PublishRetryLimit => "Failed",
                _ => "Queued"
            },
            NotificationStatus = change.NotificationStatus.ToString(),
            NotificationError = change.NotificationError
        }).ToList();
        return detail;
    }
}
