using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class MedicinePaymentTransactionRepository : IMedicinePaymentTransactionRepository
{
    private readonly AppDbContext _dbContext;

    public MedicinePaymentTransactionRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<MedicinePayment>> ListAsync(IReadOnlyCollection<MedicinePaymentStatus>? statuses, PaymentMethod? method, string? search, int take,
        CancellationToken cancellationToken = default, string? orderStatus = null, bool excludeCollected = false)
    {
        var query = _dbContext.MedicinePayments.AsNoTracking().Include(payment => payment.Items).AsQueryable();
        if (statuses is not null) query = query.Where(payment => statuses.Contains(payment.Status));
        if (orderStatus is not null) query = query.Where(payment => payment.OrderStatus == orderStatus);
        if (excludeCollected) query = query.Where(payment => payment.OrderStatus != MedicineOrderStatuses.Collected);
        if (method is not null) query = query.Where(payment => payment.Method == method);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(payment => payment.OrderNumber.ToLower().Contains(term) || payment.CustomerName.ToLower().Contains(term));
        }

        return query.OrderByDescending(payment => payment.UpdatedAtUtc).ThenByDescending(payment => payment.Id)
            .Take(take).ToListAsync(cancellationToken);
    }

    public Task<Dictionary<MedicinePaymentStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default, bool excludeCollected = false) =>
        _dbContext.MedicinePayments
            .Where(payment => !excludeCollected || payment.OrderStatus != MedicineOrderStatuses.Collected)
            .GroupBy(payment => payment.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count, cancellationToken);

    public Task<MedicinePayment?> GetDetailAsync(int paymentId, CancellationToken cancellationToken = default) =>
        _dbContext.MedicinePayments
            .Include(payment => payment.Items)
            .Include(payment => payment.Receipts)
            .Include(payment => payment.StatusHistory)
            .Include(payment => payment.OrderStatusHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken);

    public Task<int?> FindPaymentIdForOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        _dbContext.MedicinePayments.Where(payment => payment.OrderId == orderId).Select(payment => (int?)payment.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<List<EventOutbox>> GetOutboxEntriesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default) =>
        _dbContext.EventOutboxEntries.AsNoTracking().Where(entry => eventIds.Contains(entry.EventId)).ToListAsync(cancellationToken);

    public void AddOutboxEntry(EventOutbox entry) => _dbContext.EventOutboxEntries.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);

    public void DiscardChanges() => _dbContext.ChangeTracker.Clear();
}
