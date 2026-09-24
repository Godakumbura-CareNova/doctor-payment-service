namespace DoctorPayment.Api.DTOs.MedicinePayment;

/// <summary>The patient choosing how to pay. A string so an unknown or missing method gets a message, not a model-binding error.</summary>
public class SelectPaymentMethodRequestDto
{
    /// <summary>"Online", "BankTransfer" or "Cash".</summary>
    public string? Method { get; set; }
}

/// <summary>One payment method as the selection screen shows it.</summary>
public class PaymentMethodOptionDto
{
    public string Method { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Available { get; set; }

    /// <summary>Why the method cannot be chosen for this order; null when available.</summary>
    public string? UnavailableReason { get; set; }
}

/// <summary>The account to transfer to and the reference to quote.</summary>
public class BankTransferInstructionsDto
{
    public string BankName { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>The order number, so the pharmacy can match the transfer.</summary>
    public string Reference { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}

/// <summary>Everything the payment method screen needs for one order.</summary>
public class MedicinePaymentOptionsDto
{
    public int OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public List<PaymentMethodOptionDto> Methods { get; set; } = new();

    /// <summary>The method already chosen for this order, if any.</summary>
    public MedicinePaymentResponseDto? SelectedPayment { get; set; }
}

/// <summary>The chosen method and what the patient does next.</summary>
public class MedicinePaymentResponseDto
{
    public const string NextStepOnlinePayment = "OnlinePayment";
    public const string NextStepBankTransfer = "BankTransferInstructions";
    public const string NextStepCash = "PayOnCollection";

    public int Id { get; set; }

    public int OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>"Online", "BankTransfer" or "Cash".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>"AwaitingOnlinePayment", "AwaitingBankTransfer" or "PayOnCollection".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>"OnlinePayment", "BankTransferInstructions" or "PayOnCollection".</summary>
    public string NextStep { get; set; } = string.Empty;

    /// <summary>Set only for bank transfer.</summary>
    public BankTransferInstructionsDto? BankTransfer { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
