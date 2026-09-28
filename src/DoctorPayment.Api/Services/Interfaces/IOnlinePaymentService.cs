using DoctorPayment.Api.DTOs.MedicinePayment;

namespace DoctorPayment.Api.Services.Interfaces;

/// <summary>GCN-41: paying a medicine order online through PayHere (its sandbox while testing).</summary>
public interface IOnlinePaymentService
{
    /// <summary>
    /// The payment, whether it can be paid or retried now, and every try so far. Throws <see cref="KeyNotFoundException"/>
    /// when no payment method was chosen for the order.
    /// </summary>
    Task<OnlinePaymentStatusDto> GetStatusAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a try: checks the billing details and the order with the pharmacy, and returns the signed PayHere checkout
    /// for the order's saved total. Throws <see cref="ArgumentException"/> for invalid billing details,
    /// <see cref="KeyNotFoundException"/> when the order or payment is not the patient's,
    /// <see cref="Exceptions.InvalidMedicineOrderException"/> when the order cannot be paid (rejected, collected, changed),
    /// <see cref="Exceptions.PaymentMethodUnavailableException"/> when online is not the chosen method,
    /// <see cref="Exceptions.MedicinePaymentConflictException"/> when it is already paid or out of tries, and
    /// <see cref="Exceptions.PaymentGatewayUnavailableException"/> / <see cref="Exceptions.OrderServiceUnavailableException"/>
    /// when the gateway is not set up or the pharmacy cannot be reached. Starting never marks anything paid.
    /// </summary>
    Task<OnlinePaymentSessionDto> StartAsync(Guid patientId, int orderId, StartOnlinePaymentRequestDto? billing, string? authorizationHeader,
        string? origin, CancellationToken cancellationToken = default);

    /// <summary>
    /// The try's result as recorded from PayHere's notification: Succeeded, Failed, Cancelled, or Processing while PayHere
    /// has not reported yet. Throws <see cref="KeyNotFoundException"/> for a try that is not the patient's.
    /// </summary>
    Task<OnlinePaymentResultDto> CompleteAsync(Guid patientId, int orderId, int attemptId, CancellationToken cancellationToken = default);

    /// <summary>The patient closed PayHere's window without paying: an open try is cancelled (not counted as a failure).</summary>
    Task<OnlinePaymentResultDto> CancelAsync(Guid patientId, int orderId, int attemptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a result PayHere posted to the notify URL. False when it is about an unknown order. Throws
    /// <see cref="Exceptions.InvalidWebhookSignatureException"/> for an unsigned or forged result, which changes nothing.
    /// </summary>
    Task<bool> HandleNotificationAsync(IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken = default);
}
