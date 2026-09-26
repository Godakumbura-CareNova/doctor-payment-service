using System.Globalization;
using DoctorPayment.Api.Clients;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoctorPayment.Api.Services;

/// <summary>
/// Choosing how to pay for a pharmacy order. The order is always re-read from the pharmacy-order service with the
/// patient's token, so the amount and ownership come from there, never from the client.
/// </summary>
public class MedicinePaymentService : IMedicinePaymentService
{
    private const string IncompleteOrderMessage =
        "This order's details are incomplete, so payment cannot start. Please contact the pharmacy.";

    private static readonly Dictionary<PaymentMethod, (string Label, string Description)> MethodText = new()
    {
        [PaymentMethod.Online] = ("Online payment", "Pay now by card on the secure online payment page."),
        [PaymentMethod.BankTransfer] = ("Bank transfer", "Transfer the amount to the pharmacy's bank account and upload your receipt."),
        [PaymentMethod.Cash] = ("Cash", "Pay in cash at the pharmacy counter when you collect your order.")
    };

    private readonly IMedicinePaymentRepository _repository;
    private readonly IPharmacyOrderClient _orders;
    private readonly MedicinePaymentSettings _settings;
    private readonly TimeProvider _timeProvider;

    public MedicinePaymentService(
        IMedicinePaymentRepository repository,
        IPharmacyOrderClient orders,
        IOptions<MedicinePaymentSettings> settings,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _orders = orders;
        _settings = settings.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>Exact, case-insensitive method names only; numbers and comma lists are refused.</summary>
    public static bool TryParseMethod(string? value, out PaymentMethod method)
    {
        method = default;
        var name = value?.Trim();
        var match = Enum.GetNames<PaymentMethod>().FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        return match is not null && Enum.TryParse(match, out method);
    }

    public async Task<MedicinePaymentOptionsDto> GetOptionsAsync(Guid patientId, int orderId, string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        var order = await LoadPayableOrderAsync(orderId, authorizationHeader, cancellationToken);
        var selected = await _repository.GetForOrderAsync(patientId, orderId, cancellationToken);

        return new MedicinePaymentOptionsDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber!,
            Amount = order.TotalAmount,
            Methods = GetMethods(order.TotalAmount),
            SelectedPayment = selected is null ? null : ToResponse(selected, _settings)
        };
    }

    public List<PaymentMethodOptionDto> GetMethods(decimal? amount)
    {
        if (amount is < 0) throw new ArgumentException("The order amount cannot be negative.");

        return Enum.GetValues<PaymentMethod>().Select(method =>
        {
            var reason = UnavailableReason(method, amount);
            return new PaymentMethodOptionDto
            {
                Method = method.ToString(),
                Label = MethodText[method].Label,
                Description = MethodText[method].Description,
                Available = reason is null,
                UnavailableReason = reason
            };
        }).ToList();
    }

    public async Task<MedicinePaymentResponseDto> SelectAsync(Guid patientId, int orderId, SelectPaymentMethodRequestDto request, string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty) throw new ArgumentException("A patient identity is required.", nameof(patientId));
        if (!TryParseMethod(request.Method, out var method)) throw new ArgumentException("Select a payment method.");

        var order = await LoadPayableOrderAsync(orderId, authorizationHeader, cancellationToken);
        if (UnavailableReason(method, order.TotalAmount) is { } reason) throw new PaymentMethodUnavailableException(reason);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var payment = await _repository.GetForOrderAsync(patientId, orderId, cancellationToken);
        if (payment?.Status == MedicinePaymentStatus.ReceiptUnderVerification)
        {
            throw new MedicinePaymentConflictException("Your bank transfer receipt is being verified, so the payment method cannot be changed.");
        }
        if (payment?.Status == MedicinePaymentStatus.Paid)
        {
            throw new MedicinePaymentConflictException("This order has already been paid, so the payment method cannot be changed.");
        }

        if (payment is null)
        {
            payment = new MedicinePayment { OrderId = orderId, PatientId = patientId, CreatedAtUtc = now };
            _repository.Add(payment);
        }

        payment.OrderNumber = order.OrderNumber!;
        payment.Amount = order.TotalAmount;
        payment.Method = method;
        payment.Status = method switch
        {
            PaymentMethod.Online => MedicinePaymentStatus.AwaitingOnlinePayment,
            PaymentMethod.BankTransfer => MedicinePaymentStatus.AwaitingBankTransfer,
            _ => MedicinePaymentStatus.PayOnCollection
        };
        payment.UpdatedAtUtc = now;

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request chose a method for this order first; the unique order index refused this one.
            throw new MedicinePaymentConflictException("Your payment method was just changed somewhere else. Refresh and choose again.");
        }

        return ToResponse(payment, _settings);
    }

    public async Task<MedicinePaymentResponseDto?> GetSelectedAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetForOrderAsync(patientId, orderId, cancellationToken);
        return payment is null ? null : ToResponse(payment, _settings);
    }

    private async Task<PharmacyOrderSnapshot> LoadPayableOrderAsync(int orderId, string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (orderId <= 0) throw new KeyNotFoundException("We could not find this order.");

        var order = await _orders.GetOrderAsync(orderId, authorizationHeader, cancellationToken)
            ?? throw new KeyNotFoundException("We could not find this order.");

        switch (order.Status)
        {
            case "Pending":
            case "ReadyForCollection":
                break;
            case "Rejected":
                throw new InvalidMedicineOrderException("This order was rejected by the pharmacy, so it cannot be paid for.");
            case "Collected":
                throw new InvalidMedicineOrderException("This order has already been collected.");
            default:
                throw new InvalidMedicineOrderException(IncompleteOrderMessage);
        }

        var items = order.Items ?? new List<PharmacyOrderLine>();
        var itemsTotal = items.Sum(item => item.Quantity * item.UnitPrice);
        var incomplete = order.Id != orderId
            || string.IsNullOrWhiteSpace(order.OrderNumber)
            || items.Count == 0
            || items.Any(item => item.Quantity <= 0 || item.UnitPrice < 0)
            || order.TotalAmount <= 0
            || Math.Abs(itemsTotal - order.TotalAmount) > 0.01m;
        if (incomplete) throw new InvalidMedicineOrderException(IncompleteOrderMessage);

        return order;
    }

    /// <summary>Why the method cannot be used; null when it can. Without an amount the cash limit is not checked.</summary>
    private string? UnavailableReason(PaymentMethod method, decimal? amount)
    {
        var enabled = _settings.EnabledMethods is not { } names
            || names.Any(name => string.Equals(name?.Trim(), method.ToString(), StringComparison.OrdinalIgnoreCase));
        if (!enabled || (method == PaymentMethod.BankTransfer && !_settings.BankTransfer.IsConfigured))
        {
            return $"{MethodText[method].Label} is not available right now. Please choose another payment method.";
        }

        if (method == PaymentMethod.Cash && amount > _settings.CashMaxAmount)
        {
            return $"Cash is only accepted for orders up to {FormatAmount(_settings.CashMaxAmount)}. Please choose online payment or bank transfer.";
        }

        return null;
    }

    /// <summary>The payment as the patient sees it, with the bank account for a bank transfer and the latest receipt.</summary>
    public static MedicinePaymentResponseDto ToResponse(MedicinePayment payment, MedicinePaymentSettings settings) => new()
    {
        Id = payment.Id,
        OrderId = payment.OrderId,
        OrderNumber = payment.OrderNumber,
        Amount = payment.Amount,
        Method = payment.Method.ToString(),
        Status = payment.Status.ToString(),
        NextStep = payment.Method switch
        {
            PaymentMethod.Online => MedicinePaymentResponseDto.NextStepOnlinePayment,
            PaymentMethod.BankTransfer => MedicinePaymentResponseDto.NextStepBankTransfer,
            _ => MedicinePaymentResponseDto.NextStepCash
        },
        BankTransfer = payment.Method == PaymentMethod.BankTransfer
            ? new BankTransferInstructionsDto
            {
                BankName = settings.BankTransfer.BankName,
                Branch = settings.BankTransfer.Branch,
                AccountName = settings.BankTransfer.AccountName,
                AccountNumber = settings.BankTransfer.AccountNumber,
                Reference = payment.OrderNumber,
                Amount = payment.Amount
            }
            : null,
        Receipt = payment.Method == PaymentMethod.BankTransfer
            ? payment.Receipts.OrderByDescending(receipt => receipt.UploadedAtUtc).ThenByDescending(receipt => receipt.Id)
                .Select(ToReceiptDto).FirstOrDefault()
            : null,
        UpdatedAtUtc = DateTime.SpecifyKind(payment.UpdatedAtUtc, DateTimeKind.Utc)
    };

    public static BankTransferReceiptDto ToReceiptDto(BankTransferReceipt receipt) => new()
    {
        Id = receipt.Id,
        FileName = receipt.OriginalFileName,
        ContentType = receipt.ContentType,
        SizeBytes = receipt.SizeBytes,
        Status = receipt.Status.ToString(),
        UploadedAtUtc = DateTime.SpecifyKind(receipt.UploadedAtUtc, DateTimeKind.Utc),
        ReviewedAtUtc = receipt.ReviewedAtUtc is { } reviewed ? DateTime.SpecifyKind(reviewed, DateTimeKind.Utc) : null,
        RejectionReason = receipt.RejectionReason
    };

    private static string FormatAmount(decimal amount) => $"LKR {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
