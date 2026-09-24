namespace DoctorPayment.Api.Config;

/// <summary>Which medicine payment methods are offered and the rules around them. Bound from "MedicinePayments".</summary>
public class MedicinePaymentSettings
{
    /// <summary>
    /// Methods offered to patients ("Online", "BankTransfer", "Cash"); null offers all of them. A method left out is shown
    /// as unavailable. An array, not a list, so configuration replaces it instead of appending to a default.
    /// </summary>
    public string[]? EnabledMethods { get; set; }

    /// <summary>Largest order total that can be paid in cash at collection.</summary>
    public decimal CashMaxAmount { get; set; } = 25_000m;

    /// <summary>The account patients transfer to. Bank transfer is unavailable until this is configured.</summary>
    public BankTransferSettings BankTransfer { get; set; } = new();
}

public class BankTransferSettings
{
    public string BankName { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BankName) && !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountNumber);
}

/// <summary>Where the pharmacy-order API lives (including its "/api" prefix). Bound from "PharmacyOrderService".</summary>
public class PharmacyOrderServiceSettings
{
    public string BaseUrl { get; set; } = string.Empty;
}
