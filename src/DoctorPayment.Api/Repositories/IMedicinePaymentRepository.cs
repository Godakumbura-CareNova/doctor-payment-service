using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>Storage for the payment method chosen for each pharmacy order.</summary>
public interface IMedicinePaymentRepository
{
    /// <summary>The patient's payment for the order (tracked), or null when no method has been chosen.</summary>
    Task<MedicinePayment?> GetForOrderAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default);

    void Add(MedicinePayment payment);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
