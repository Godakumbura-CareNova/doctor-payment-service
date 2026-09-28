namespace DoctorPayment.Api.Config;

/// <summary>
/// GCN-41: the PayHere payment gateway (Sri Lanka) for online medicine payments. Bound from "PayHere". The merchant
/// secret is a secret: set it with user secrets or an environment variable, never in appsettings. The sandbox is used
/// unless live payments are explicitly allowed, so no real card can be charged while the system is being tested.
/// </summary>
public class PayHereSettings
{
    /// <summary>The merchant ID from the PayHere (sandbox) account.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The merchant secret PayHere gives for the registered domain. Used only on the server, to sign and verify.</summary>
    public string MerchantSecret { get; set; } = string.Empty;

    /// <summary>True for the PayHere sandbox (test cards, no real money).</summary>
    public bool Sandbox { get; set; } = true;

    /// <summary>Live payments charge real cards; they are refused unless this is true.</summary>
    public bool AllowLivePayments { get; set; }

    /// <summary>
    /// Where PayHere posts the payment result: this service's public HTTPS address of
    /// <c>api/payments/payhere/notify</c>. It must be reachable from the internet (not localhost).
    /// </summary>
    public string NotifyUrl { get; set; } = string.Empty;

    /// <summary>The frontend's public address, for PayHere's return and cancel links; the request's origin is used when empty.</summary>
    public string FrontendBaseUrl { get; set; } = string.Empty;

    public string Currency { get; set; } = "LKR";

    /// <summary>How many online payment attempts a patient gets for one order before they must choose another method.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The merchant ID, secret and notify URL are set, and live payments are only used when allowed.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MerchantId)
        && !string.IsNullOrWhiteSpace(MerchantSecret)
        && Uri.TryCreate(NotifyUrl, UriKind.Absolute, out var notify) && (notify.Scheme == Uri.UriSchemeHttps || notify.Scheme == Uri.UriSchemeHttp)
        && (Sandbox || AllowLivePayments);
}
