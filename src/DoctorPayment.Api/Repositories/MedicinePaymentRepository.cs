using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class MedicinePaymentRepository : IMedicinePaymentRepository
{
    private readonly AppDbContext _dbContext;

    public MedicinePaymentRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<MedicinePayment?> GetForOrderAsync(Guid patientId, int orderId, CancellationToken cancellationToken = default) =>
        _dbContext.MedicinePayments.SingleOrDefaultAsync(payment => payment.OrderId == orderId && payment.PatientId == patientId, cancellationToken);

    public void Add(MedicinePayment payment) => _dbContext.MedicinePayments.Add(payment);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);
}
