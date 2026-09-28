using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

public enum OnlinePaymentAttemptStatus
{
    /// <summary>The card payment was started at the gateway and has no final result yet.</summary>
    Pending,

    /// <summary>The gateway confirmed the payment; the medicine payment is Paid.</summary>
    Succeeded,

    /// <summary>The card was declined or the result could not be trusted; the order is not paid.</summary>
    Failed,

    /// <summary>The patient closed the payment window (or cancelled at PayHere) before paying. Not counted as a failed try.</summary>
    Cancelled
}

/// <summary>
/// GCN-41: one try at paying a medicine order online by card. Every try is kept, so a declined payment and the retry
/// that succeeded can both be traced. No card details are stored: only the gateway's reference, the result and any
/// decline code.
/// </summary>
public class OnlinePaymentAttempt
{
    public int Id { get; set; }

    public int MedicinePaymentId { get; set; }

    public MedicinePayment? MedicinePayment { get; set; }

    public int OrderId { get; set; }

    public Guid PatientId { get; set; }

    /// <summary>1 for the first try on this payment, 2 for the first retry, and so on.</summary>
    public int AttemptNumber { get; set; }

    /// <summary>The order id sent to the gateway for this try, e.g. "CN-000046-2"; unique, so each try is a separate PayHere payment.</summary>
    [MaxLength(100)]
    public string GatewayReference { get; set; } = string.Empty;

    /// <summary>PayHere's own payment id, once it has reported a result.</summary>
    [MaxLength(50)]
    public string? GatewayPaymentId { get; set; }

    /// <summary>How the patient paid at PayHere, e.g. "VISA" or "MASTER". No card number is kept.</summary>
    [MaxLength(30)]
    public string? PaymentMethod { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = string.Empty;

    public OnlinePaymentAttemptStatus Status { get; set; }

    /// <summary>True when the gateway ran in its sandbox (test mode), so no real money moved.</summary>
    public bool IsTest { get; set; }

    /// <summary>Why the try did not succeed, e.g. PayHere's status code "-2" (failed) or "verification_failed".</summary>
    [MaxLength(100)]
    public string? FailureCode { get; set; }

    [MaxLength(500)]
    public string? FailureMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>The Kafka event that announced the result, to show whether the patient was notified.</summary>
    public Guid? ResultEventId { get; set; }
}
