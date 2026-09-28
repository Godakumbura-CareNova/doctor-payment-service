using System.Text.Json;

namespace DoctorPayment.Api.Events;

/// <summary>
/// The envelope of every event on <see cref="EventTypes.PaymentTopic"/> and <see cref="EventTypes.OrderStatusTopic"/>.
/// The aggregate is the medicine order.
/// </summary>
public sealed record PaymentEvent(
    Guid EventId,
    string EventType,
    DateTime OccurredAtUtc,
    int AggregateId,
    string AggregateType,
    JsonElement Data);

/// <summary>The data of a bank receipt event: which order, who paid, and what the pharmacy has to do.</summary>
public sealed record BankReceiptEventData(
    int PaymentId,
    int OrderId,
    string OrderNumber,
    decimal Amount,
    Guid PatientId,
    string CustomerName,
    string FileName,
    string PaymentStatus,
    string Message,
    IReadOnlyList<BankReceiptEventItem>? Items = null);

/// <summary>One medicine on the order and how many were ordered, e.g. 2 x Iodex.</summary>
public sealed record BankReceiptEventItem(string MedicineName, int Quantity);

/// <summary>The data of an order status event: which order and patient, and the status before and after.</summary>
public sealed record OrderStatusEventData(
    int OrderId,
    string OrderNumber,
    int PaymentId,
    Guid PatientId,
    string CustomerName,
    string PreviousStatus,
    string NewStatus,
    string? Reason,
    DateTime ChangedAtUtc,
    string Message);

/// <summary>
/// GCN-42: the data of a bank transfer validation event: which order and patient, the verification result and the
/// transaction details staff recorded.
/// </summary>
public sealed record BankTransferValidationEventData(
    int ReceiptId,
    int PaymentId,
    int OrderId,
    string OrderNumber,
    decimal Amount,
    Guid PatientId,
    string CustomerName,
    string Result,
    string PaymentStatus,
    string? TransactionReference,
    decimal? TransferredAmount,
    string? Reason,
    Guid ReviewedBy,
    DateTime ReviewedAtUtc,
    string Message);

/// <summary>
/// GCN-41: the data of an online payment event: which order and patient, the gateway reference, whether it ran in the
/// sandbox, and why it failed.
/// </summary>
public sealed record OnlinePaymentEventData(
    int AttemptId,
    int PaymentId,
    int OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    Guid PatientId,
    string CustomerName,
    int AttemptNumber,
    string GatewayReference,
    bool IsTest,
    string Result,
    string PaymentStatus,
    string? FailureCode,
    string? FailureMessage,
    bool CanRetry,
    DateTime OccurredAtUtc,
    string Message);
