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
using DoctorPayment.Api.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

/// <summary>
/// GCN-41: paying a medicine order online through PayHere. The amount always comes from the saved payment, re-checked
/// against the pharmacy's order, and the checkout is signed here so it cannot be changed in the browser. The card is
/// typed into PayHere's own payment window. Only a result PayHere posts to the notify URL, with a valid signature and the
/// right amount and currency, marks the payment Paid; what the browser says is never trusted. Each result is saved
/// together with its Kafka outbox event.
/// </summary>
public class OnlinePaymentService : IOnlinePaymentService
{
    public const string PaymentRole = "PaymentGateway";
    public const string SucceededMessage = "Your payment was successful. Your order is paid.";
    public const string ProcessingMessage = "We are waiting for PayHere to confirm your payment. This usually takes a few seconds.";
    public const string CancelledMessage = "The payment was not completed in PayHere's window. Your order has not been charged.";
    public const string CancelledCode = "cancelled";
    public const string VerificationFailedMessage = "The payment could not be verified, so your order was not marked as paid. Please try again.";
    public const string DeclinedMessage = "Your card payment was declined.";

    private static readonly StartOnlinePaymentValidator BillingValidator = new();

    private readonly IOnlinePaymentRepository _repository;
    private readonly IPharmacyOrderClient _orders;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly MedicinePaymentSettings _paymentSettings;
    private readonly PayHereSettings _payHereSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OnlinePaymentService> _logger;

    public OnlinePaymentService(
        IOnlinePaymentRepository repository,
        IPharmacyOrderClient orders,
        IOnlinePaymentGateway gateway,
        IOptions<MedicinePaymentSettings> paymentSettings,
        IOptions<PayHereSettings> payHereSettings,
        TimeProvider timeProvider,
        ILogger<OnlinePaymentService> logger)
    {
        _repository = repository;
        _orders = orders;
        _gateway = gateway;
        _paymentSettings = paymentSettings.Value;
        _payHereSettings = payHereSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private int MaxAttempts => Math.Max(1, _payHereSettings.MaxAttempts);

    public async Task<OnlinePaymentStatusDto> GetStatusAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetPaymentAsync(patientId, orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Choose a payment method for this order first.");
        var attempts = await _repository.ListAttemptsAsync(payment.Id, cancellationToken);
        var reason = CannotPayReason(payment, attempts) ?? (_gateway.IsConfigured ? null : PayHerePaymentGateway.UnavailableMessage);

        return new OnlinePaymentStatusDto
        {
            Payment = MedicinePaymentService.ToResponse(payment, _paymentSettings),
            GatewayAvailable = _gateway.IsConfigured,
            IsTestMode = _gateway.IsTestMode,
            CanPay = reason is null,
            CannotPayReason = reason,
            AttemptsUsed = FailedCount(attempts),
            MaxAttempts = MaxAttempts,
            Attempts = attempts.OrderByDescending(attempt => attempt.AttemptNumber).Select(ToDto).ToList()
        };
    }

    public async Task<OnlinePaymentSessionDto> StartAsync(Guid patientId, int orderId, StartOnlinePaymentRequestDto? billing, string? authorizationHeader,
        string? origin, CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty) throw new ArgumentException("A patient identity is required.", nameof(patientId));
        var validation = BillingValidator.Validate(billing ?? new StartOnlinePaymentRequestDto());
        if (!validation.IsValid) throw new ArgumentException(validation.Errors[0].ErrorMessage);
        if (orderId <= 0) throw new KeyNotFoundException("We could not find this order.");

        var payment = await _repository.GetPaymentAsync(patientId, orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Choose a payment method for this order first.");
        var attempts = await _repository.ListAttemptsAsync(payment.Id, cancellationToken);
        if (CannotPayReason(payment, attempts) is { } reason)
        {
            throw payment.Status == MedicinePaymentStatus.Paid || FailedCount(attempts) >= MaxAttempts
                ? new MedicinePaymentConflictException(reason)
                : new PaymentMethodUnavailableException(reason);
        }
        await EnsureOrderIsPayableAsync(payment, authorizationHeader, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // A try left unfinished (its window closed without a result) counts as a failed try before a new one starts.
        // If PayHere still reports it as paid later, that result is applied (see HandleNotificationAsync).
        foreach (var open in attempts.Where(attempt => attempt.Status == OnlinePaymentAttemptStatus.Pending))
        {
            FailUnfinished(open, payment, attempts, now);
        }
        if (FailedCount(attempts) >= MaxAttempts) throw new MedicinePaymentConflictException(CannotPayReason(payment, attempts)!);

        var attemptNumber = attempts.Count == 0 ? 1 : attempts.Max(attempt => attempt.AttemptNumber) + 1;
        var gatewayOrderId = $"{payment.OrderNumber}-{attemptNumber}";
        var frontend = FrontendBaseUrl(origin);
        var paymentPage = $"{frontend}/patient/orders/{payment.OrderId}/payment/online";
        var customer = new GatewayCustomer(Clean(billing!.FirstName), Clean(billing.LastName), Clean(billing.Email), NormalizePhone(billing.Phone!),
            Clean(billing.Address), Clean(billing.City), "Sri Lanka");
        // Signed before anything is saved, so an unconfigured gateway leaves no half-started try behind.
        var checkout = _gateway.CreateCheckout(new GatewayCheckoutRequest(gatewayOrderId, $"CareNova medicine order {payment.OrderNumber}",
            payment.Amount, customer, paymentPage, paymentPage, payment.Id.ToString(CultureInfo.InvariantCulture)));

        var attempt = new OnlinePaymentAttempt
        {
            MedicinePaymentId = payment.Id,
            OrderId = payment.OrderId,
            PatientId = patientId,
            AttemptNumber = attemptNumber,
            GatewayReference = gatewayOrderId,
            Amount = payment.Amount,
            Currency = checkout.Currency,
            Status = OnlinePaymentAttemptStatus.Pending,
            IsTest = checkout.Sandbox,
            CreatedAtUtc = now
        };
        _repository.Add(attempt);
        await SaveAsync(cancellationToken);
        return ToSession(attempt, checkout, payment);
    }

    public async Task<OnlinePaymentResultDto> CompleteAsync(Guid patientId, int orderId, int attemptId, CancellationToken cancellationToken = default)
    {
        var (payment, attempts, attempt) = await LoadAttemptAsync(patientId, orderId, attemptId, cancellationToken);
        return ToResult(attempt, payment, attempts);
    }

    public async Task<OnlinePaymentResultDto> CancelAsync(Guid patientId, int orderId, int attemptId, CancellationToken cancellationToken = default)
    {
        var (payment, attempts, attempt) = await LoadAttemptAsync(patientId, orderId, attemptId, cancellationToken);
        if (attempt.Status == OnlinePaymentAttemptStatus.Pending)
        {
            FailUnfinished(attempt, payment, attempts, _timeProvider.GetUtcNow().UtcDateTime);
            await SaveAsync(cancellationToken);
        }
        return ToResult(attempt, payment, attempts);
    }

    public async Task<bool> HandleNotificationAsync(IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken = default)
    {
        var result = _gateway.ReadNotification(form);
        var attempt = await _repository.GetByGatewayReferenceAsync(result.OrderId, cancellationToken);
        if (attempt?.MedicinePayment is not { } payment)
        {
            _logger.LogWarning("PayHere result for unknown order {OrderId} ignored", result.OrderId);
            return false;
        }

        // A result is applied once. Closing PayHere's window only ends a try as far as the browser knows, so a late
        // "paid" from PayHere for that try still counts: the patient was charged.
        var closedInBrowser = attempt.Status == OnlinePaymentAttemptStatus.Cancelled
            || (attempt.Status == OnlinePaymentAttemptStatus.Failed && attempt.FailureCode == CancelledCode);
        var open = attempt.Status == OnlinePaymentAttemptStatus.Pending || (closedInBrowser && result.StatusCode == 2);
        var chargeback = attempt.Status == OnlinePaymentAttemptStatus.Succeeded && result.StatusCode == -3;
        if (!open && !chargeback) return true;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        attempt.GatewayPaymentId = Truncate(result.PaymentId, 50) ?? attempt.GatewayPaymentId;
        attempt.PaymentMethod = Truncate(result.Method, 30) ?? attempt.PaymentMethod;

        if (Math.Abs(result.Amount - attempt.Amount) > 0.001m || !string.Equals(result.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PayHere result for {OrderId} was for {Amount} {Currency}, not {Expected} {ExpectedCurrency}; not treated as paid",
                result.OrderId, result.Amount, result.Currency, attempt.Amount, attempt.Currency);
            Fail(attempt, payment, "verification_failed", VerificationFailedMessage, now);
        }
        else
        {
            switch (result.StatusCode)
            {
                case 2:
                    attempt.Status = OnlinePaymentAttemptStatus.Succeeded;
                    attempt.FailureCode = null;
                    attempt.FailureMessage = null;
                    attempt.CompletedAtUtc = now;
                    if (payment.Status != MedicinePaymentStatus.Paid)
                    {
                        AddHistory(payment, MedicinePaymentStatus.Paid, attempt,
                            $"Online payment {attempt.GatewayReference} succeeded at PayHere{(attempt.IsTest ? " (sandbox test payment)" : string.Empty)}.", now);
                        payment.Status = MedicinePaymentStatus.Paid;
                        payment.UpdatedAtUtc = now;
                    }
                    break;
                case -2:
                    Fail(attempt, payment, "-2", string.IsNullOrWhiteSpace(result.StatusMessage) ? DeclinedMessage : result.StatusMessage!, now);
                    break;
                case -3:
                    // A charge-back takes back a payment that had succeeded, so the order is no longer paid.
                    Fail(attempt, payment, "-3", "The payment was charged back by the card issuer.", now, reversePaid: true);
                    break;
                case -1:
                    Fail(attempt, payment, CancelledCode, CancelledMessage, now);
                    break;
                default:
                    // 0 = pending at PayHere: wait for the final result.
                    await SaveAsync(cancellationToken);
                    return true;
            }
        }

        var attempts = await _repository.ListAttemptsAsync(payment.Id, cancellationToken);
        var canRetry = attempt.Status == OnlinePaymentAttemptStatus.Failed && payment.Status != MedicinePaymentStatus.Paid && FailedCount(attempts) < MaxAttempts;
        attempt.ResultEventId = AddEvent(attempt, payment, canRetry, now);
        await SaveAsync(cancellationToken);
        return true;
    }

    private async Task<(MedicinePayment Payment, List<OnlinePaymentAttempt> Attempts, OnlinePaymentAttempt Attempt)> LoadAttemptAsync(
        Guid patientId, int orderId, int attemptId, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetPaymentAsync(patientId, orderId, cancellationToken)
            ?? throw new KeyNotFoundException("We could not find this payment.");
        var attempts = await _repository.ListAttemptsAsync(payment.Id, cancellationToken);
        // Only the patient's own try on this order.
        var attempt = attempts.SingleOrDefault(item => item.Id == attemptId)
            ?? throw new KeyNotFoundException("We could not find this payment attempt.");
        return (payment, attempts, attempt);
    }

    /// <summary>Why the patient cannot pay online now, or null when they can.</summary>
    private string? CannotPayReason(MedicinePayment payment, IReadOnlyCollection<OnlinePaymentAttempt> attempts)
    {
        if (payment.Status == MedicinePaymentStatus.Paid) return "This order has already been paid.";
        if (payment.Method != PaymentMethod.Online) return "Online payment is not the payment method for this order. Choose online payment first.";
        if (payment.Status is not (MedicinePaymentStatus.AwaitingOnlinePayment or MedicinePaymentStatus.Failed))
        {
            return "This payment cannot be paid online right now.";
        }
        if (FailedCount(attempts) >= MaxAttempts)
        {
            return $"Online payment failed {MaxAttempts} times for this order, so it cannot be retried. Please choose another payment method.";
        }
        return null;
    }

    /// <summary>Every unsuccessful try counts, including ones the patient closed (older rows may still say Cancelled).</summary>
    private static int FailedCount(IEnumerable<OnlinePaymentAttempt> attempts) =>
        attempts.Count(attempt => attempt.Status is OnlinePaymentAttemptStatus.Failed or OnlinePaymentAttemptStatus.Cancelled);

    /// <summary>A try that ended without a result from PayHere is a failed try: recorded, counted, and the patient notified.</summary>
    private void FailUnfinished(OnlinePaymentAttempt attempt, MedicinePayment payment, IReadOnlyCollection<OnlinePaymentAttempt> attempts, DateTime now)
    {
        Fail(attempt, payment, CancelledCode, CancelledMessage, now);
        var canRetry = payment.Status != MedicinePaymentStatus.Paid && FailedCount(attempts) < MaxAttempts;
        attempt.ResultEventId = AddEvent(attempt, payment, canRetry, now);
    }

    /// <summary>The pharmacy's order must still be open, and the same order number and total the patient is paying for.</summary>
    private async Task EnsureOrderIsPayableAsync(MedicinePayment payment, string? authorizationHeader, CancellationToken cancellationToken)
    {
        var order = await _orders.GetOrderAsync(payment.OrderId, authorizationHeader, cancellationToken)
            ?? throw new KeyNotFoundException("We could not find this order.");
        switch (order.Status)
        {
            case MedicineOrderStatuses.Rejected:
                throw new InvalidMedicineOrderException("This order was rejected by the pharmacy, so it cannot be paid for.");
            case MedicineOrderStatuses.Collected:
                throw new InvalidMedicineOrderException("This order has already been collected, so it cannot be paid for online.");
        }
        if (order.TotalAmount <= 0 || payment.Amount <= 0)
        {
            throw new InvalidMedicineOrderException("This order's total is not valid, so payment cannot start. Please contact the pharmacy.");
        }
        if (!string.Equals(order.OrderNumber, payment.OrderNumber, StringComparison.Ordinal) || Math.Abs(order.TotalAmount - payment.Amount) > 0.01m)
        {
            throw new InvalidMedicineOrderException(
                "This order has changed since you chose online payment, so payment cannot start. Choose the payment method again, then pay.");
        }
    }

    private static void Fail(OnlinePaymentAttempt attempt, MedicinePayment payment, string code, string message, DateTime now, bool reversePaid = false)
    {
        attempt.Status = OnlinePaymentAttemptStatus.Failed;
        attempt.FailureCode = Truncate(code, 100);
        attempt.FailureMessage = Truncate(message, 500);
        attempt.CompletedAtUtc = now;
        if (payment.Status == MedicinePaymentStatus.Paid && !reversePaid) return;
        AddHistory(payment, MedicinePaymentStatus.Failed, attempt, $"Online payment {attempt.GatewayReference} failed: {message}", now);
        payment.Status = MedicinePaymentStatus.Failed;
        payment.UpdatedAtUtc = now;
    }

    private Guid AddEvent(OnlinePaymentAttempt attempt, MedicinePayment payment, bool canRetry, DateTime now)
    {
        var eventId = Guid.NewGuid();
        var succeeded = attempt.Status == OnlinePaymentAttemptStatus.Succeeded;
        var eventType = succeeded ? EventTypes.OnlinePaymentSucceeded : EventTypes.OnlinePaymentFailed;
        var data = new OnlinePaymentEventData(attempt.Id, payment.Id, payment.OrderId, payment.OrderNumber, attempt.Amount, attempt.Currency,
            payment.PatientId, payment.CustomerName, attempt.AttemptNumber, attempt.GatewayReference, attempt.IsTest,
            attempt.Status.ToString(), payment.Status.ToString(), attempt.FailureCode, attempt.FailureMessage, canRetry, now,
            succeeded ? "Online payment succeeded; the order is paid." : $"Online payment failed: {attempt.FailureMessage}");
        var paymentEvent = new PaymentEvent(eventId, eventType, now, payment.OrderId, "MedicineOrder", JsonSerializer.SerializeToElement(data));
        _repository.AddOutboxEntry(new EventOutbox
        {
            EventId = eventId,
            Topic = EventTypes.OnlinePaymentTopic,
            EventType = eventType,
            EventKey = payment.OrderId.ToString(CultureInfo.InvariantCulture),
            OccurredAtUtc = now,
            PayloadJson = JsonSerializer.Serialize(paymentEvent)
        });
        return eventId;
    }

    private static void AddHistory(MedicinePayment payment, MedicinePaymentStatus toStatus, OnlinePaymentAttempt attempt, string note, DateTime now) =>
        payment.StatusHistory.Add(new MedicinePaymentStatusChange
        {
            FromStatus = payment.Status,
            ToStatus = toStatus,
            ChangedBy = attempt.PatientId,
            ChangedByRole = PaymentRole,
            Note = Truncate(note, 500),
            ChangedAtUtc = now
        });

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _repository.DiscardChanges();
            _logger.LogWarning(exception, "Online payment change could not be saved");
            throw new MedicinePaymentConflictException("This payment was just changed somewhere else. Refresh the page and try again.");
        }
    }

    /// <summary>The configured frontend address, or else the calling page's origin; PayHere needs absolute return links.</summary>
    private string FrontendBaseUrl(string? origin)
    {
        var configured = _payHereSettings.FrontendBaseUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured)) return configured;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.GetLeftPart(UriPartial.Authority)
            : "https://www.payhere.lk";
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    /// <summary>0771234567 and +94 77 123 4567 both become 0771234567.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0094", StringComparison.Ordinal)) digits = digits[2..];
        return digits.StartsWith("94", StringComparison.Ordinal) && digits.Length == 11 ? "0" + digits[2..] : digits;
    }

    private static OnlinePaymentSessionDto ToSession(OnlinePaymentAttempt attempt, GatewayCheckout checkout, MedicinePayment payment) => new()
    {
        AttemptId = attempt.Id,
        AttemptNumber = attempt.AttemptNumber,
        Sandbox = checkout.Sandbox,
        MerchantId = checkout.MerchantId,
        GatewayOrderId = checkout.OrderId,
        Items = checkout.Items,
        Amount = checkout.Amount,
        Currency = checkout.Currency,
        Hash = checkout.Hash,
        NotifyUrl = checkout.NotifyUrl,
        ReturnUrl = checkout.ReturnUrl,
        CancelUrl = checkout.CancelUrl,
        FirstName = checkout.Customer.FirstName,
        LastName = checkout.Customer.LastName,
        Email = checkout.Customer.Email,
        Phone = checkout.Customer.Phone,
        Address = checkout.Customer.Address,
        City = checkout.Customer.City,
        Country = checkout.Customer.Country,
        Custom1 = checkout.Custom1,
        OrderNumber = payment.OrderNumber
    };

    private OnlinePaymentResultDto ToResult(OnlinePaymentAttempt attempt, MedicinePayment payment, IReadOnlyCollection<OnlinePaymentAttempt> attempts)
    {
        var (outcome, message) = attempt.Status switch
        {
            OnlinePaymentAttemptStatus.Succeeded => (OnlinePaymentResultDto.Succeeded,
                attempt.IsTest ? SucceededMessage + " (PayHere sandbox test payment: no real money was charged.)" : SucceededMessage),
            OnlinePaymentAttemptStatus.Failed => (OnlinePaymentResultDto.Failed,
                $"Payment failed: {attempt.FailureMessage} Your order has not been marked as paid."),
            OnlinePaymentAttemptStatus.Cancelled => (OnlinePaymentResultDto.Failed, $"Payment failed: {CancelledMessage}"),
            _ => (OnlinePaymentResultDto.Processing, ProcessingMessage)
        };
        var retryable = attempt.Status is OnlinePaymentAttemptStatus.Failed or OnlinePaymentAttemptStatus.Cancelled;
        return new OnlinePaymentResultDto
        {
            Outcome = outcome,
            Message = message,
            CanRetry = retryable && payment.Status != MedicinePaymentStatus.Paid && FailedCount(attempts) < MaxAttempts,
            IsTest = attempt.IsTest,
            TransactionReference = attempt.GatewayReference,
            FailureCode = attempt.FailureCode,
            Payment = MedicinePaymentService.ToResponse(payment, _paymentSettings)
        };
    }

    private static OnlinePaymentAttemptDto ToDto(OnlinePaymentAttempt attempt) => new()
    {
        Id = attempt.Id,
        AttemptNumber = attempt.AttemptNumber,
        Status = attempt.Status.ToString(),
        PaymentMethod = attempt.PaymentMethod,
        Amount = attempt.Amount,
        Currency = attempt.Currency,
        TransactionReference = attempt.GatewayReference,
        IsTest = attempt.IsTest,
        FailureMessage = attempt.FailureMessage,
        CreatedAtUtc = DateTime.SpecifyKind(attempt.CreatedAtUtc, DateTimeKind.Utc),
        CompletedAtUtc = attempt.CompletedAtUtc is { } completed ? DateTime.SpecifyKind(completed, DateTimeKind.Utc) : null
    };

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
