using DoctorPayment.Api.DTOs.MedicinePayment;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IMedicinePaymentService
{
    /// <summary>
    /// The payment methods for an order of this total, before the order is placed (checkout). Advisory only: the choice
    /// is checked again against the real order when it is saved. Throws <see cref="ArgumentException"/> for a negative amount.
    /// </summary>
    List<PaymentMethodOptionDto> GetMethods(decimal? amount);

    /// <summary>
    /// The payment methods for the patient's order, each marked available or not, plus any method already chosen.
    /// Throws <see cref="KeyNotFoundException"/> when the order is not the patient's,
    /// <see cref="Exceptions.InvalidMedicineOrderException"/> when it cannot be paid for, and
    /// <see cref="Exceptions.OrderServiceUnavailableException"/> when the order cannot be loaded.
    /// </summary>
    Task<MedicinePaymentOptionsDto> GetOptionsAsync(Guid patientId, int orderId, string? authorizationHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records (or changes) the method for the order and says what happens next. Same exceptions as
    /// <see cref="GetOptionsAsync"/>, plus <see cref="ArgumentException"/> for a missing or unknown method and
    /// <see cref="Exceptions.PaymentMethodUnavailableException"/> when the method is not offered for this order.
    /// </summary>
    Task<MedicinePaymentResponseDto> SelectAsync(Guid patientId, int orderId, SelectPaymentMethodRequestDto request, string? authorizationHeader, CancellationToken cancellationToken = default);

    /// <summary>The method chosen for the order, or null when none has been chosen yet.</summary>
    Task<MedicinePaymentResponseDto?> GetSelectedAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default);
}
