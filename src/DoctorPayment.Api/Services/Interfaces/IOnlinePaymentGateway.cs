namespace DoctorPayment.Api.Services.Interfaces;

/// <summary>The customer details the gateway's checkout needs (PayHere requires all of them).</summary>
public sealed record GatewayCustomer(string FirstName, string LastName, string Email, string Phone, string Address, string City, string Country);

/// <summary>What the service asks the gateway to collect for one attempt. <see cref="OrderId"/> is unique per attempt.</summary>
public sealed record GatewayCheckoutRequest(
    string OrderId,
    string ItemsDescription,
    decimal Amount,
    GatewayCustomer Customer,
    string ReturnUrl,
    string CancelUrl,
    string Custom1);

/// <summary>
/// The signed checkout the browser opens with the gateway's own payment window. It holds no secret: the hash proves the
/// merchant, order, amount and currency were set by this service and cannot be changed in the browser.
/// </summary>
public sealed record GatewayCheckout(
    bool Sandbox,
    string MerchantId,
    string OrderId,
    string Items,
    string Amount,
    string Currency,
    string Hash,
    string NotifyUrl,
    string ReturnUrl,
    string CancelUrl,
    GatewayCustomer Customer,
    string Custom1);

/// <summary>
/// A payment result the gateway posted to the notify URL, after its signature was checked. <see cref="StatusCode"/> uses
/// PayHere's codes: 2 success, 0 pending, -1 cancelled, -2 failed, -3 charged back.
/// </summary>
public sealed record GatewayNotification(
    string OrderId,
    string? PaymentId,
    decimal Amount,
    string Currency,
    int StatusCode,
    string? StatusMessage,
    string? Method);

/// <summary>
/// GCN-41: the online payment gateway (PayHere). Card details never reach this service: the patient types them into the
/// gateway's own payment window. This service signs the checkout and verifies the signed result the gateway posts back.
/// </summary>
public interface IOnlinePaymentGateway
{
    /// <summary>The merchant ID, secret and notify URL are set (and it is the sandbox unless live payments are allowed).</summary>
    bool IsConfigured { get; }

    /// <summary>True when the gateway runs in its sandbox, so test cards are used and no real money moves.</summary>
    bool IsTestMode { get; }

    /// <summary>The currency the gateway charges in, e.g. "LKR".</summary>
    string Currency { get; }

    /// <summary>Signs a checkout. Throws <see cref="Exceptions.PaymentGatewayUnavailableException"/> when the gateway is not configured.</summary>
    GatewayCheckout CreateCheckout(GatewayCheckoutRequest request);

    /// <summary>
    /// Reads a result the gateway posted. Throws <see cref="Exceptions.InvalidWebhookSignatureException"/> when the signature
    /// or merchant is wrong, or a field is missing, so a forged result is never applied.
    /// </summary>
    GatewayNotification ReadNotification(IReadOnlyDictionary<string, string> form);
}
