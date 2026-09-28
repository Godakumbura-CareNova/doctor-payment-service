namespace DoctorPayment.Api.DTOs.MedicinePayment;

/// <summary>GCN-41: one online payment try as the patient sees it.</summary>
public class OnlinePaymentAttemptDto
{
    public int Id { get; set; }

    public int AttemptNumber { get; set; }

    /// <summary>"Pending", "Succeeded", "Failed" or "Cancelled".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>How the patient paid at PayHere, e.g. "VISA"; null until a result came back.</summary>
    public string? PaymentMethod { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>The order id this try used at PayHere, e.g. "CN-000046-2".</summary>
    public string TransactionReference { get; set; } = string.Empty;

    public bool IsTest { get; set; }

    public string? FailureMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>GCN-41: everything the online payment page needs before the patient pays.</summary>
public class OnlinePaymentStatusDto
{
    public MedicinePaymentResponseDto Payment { get; set; } = new();

    /// <summary>The gateway is configured and can take payments.</summary>
    public bool GatewayAvailable { get; set; }

    /// <summary>The gateway runs in its sandbox: test cards only, no real money.</summary>
    public bool IsTestMode { get; set; }

    /// <summary>The payment can be made (or retried) now.</summary>
    public bool CanPay { get; set; }

    /// <summary>Why the payment cannot be made now; null when it can.</summary>
    public string? CannotPayReason { get; set; }

    public int AttemptsUsed { get; set; }

    public int MaxAttempts { get; set; }

    /// <summary>Every try, newest first.</summary>
    public List<OnlinePaymentAttemptDto> Attempts { get; set; } = new();
}

/// <summary>GCN-41: the billing details PayHere's checkout needs. Only sent on to PayHere; not stored here.</summary>
public class StartOnlinePaymentRequestDto
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    /// <summary>A Sri Lankan phone number, e.g. 0771234567 or +94771234567.</summary>
    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }
}

/// <summary>
/// GCN-41: a started payment: the signed PayHere checkout the browser opens in PayHere's payment window. It holds no
/// secret; the hash stops the amount or order being changed in the browser.
/// </summary>
public class OnlinePaymentSessionDto
{
    public int AttemptId { get; set; }

    public int AttemptNumber { get; set; }

    public bool Sandbox { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The order id for this try at PayHere, e.g. "CN-000046-2".</summary>
    public string GatewayOrderId { get; set; } = string.Empty;

    public string Items { get; set; } = string.Empty;

    /// <summary>The amount exactly as signed, e.g. "450.00".</summary>
    public string Amount { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public string NotifyUrl { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;

    public string CancelUrl { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    /// <summary>Passed through PayHere and back on the result: the medicine payment id.</summary>
    public string Custom1 { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;
}

/// <summary>GCN-41: the result of a card payment, as confirmed by the server with the gateway.</summary>
public class OnlinePaymentResultDto
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Processing = "Processing";
    public const string Cancelled = "Cancelled";

    /// <summary>"Succeeded", "Failed", "Cancelled" or "Processing" (PayHere has not reported the result yet).</summary>
    public string Outcome { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The patient may try again (a failed or cancelled payment with tries left).</summary>
    public bool CanRetry { get; set; }

    public bool IsTest { get; set; }

    public string TransactionReference { get; set; } = string.Empty;

    public string? FailureCode { get; set; }

    public MedicinePaymentResponseDto Payment { get; set; } = new();
}
