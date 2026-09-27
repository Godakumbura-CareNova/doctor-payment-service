using DoctorPayment.Api.Data;
using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Repositories;

public class BankTransferReceiptRepository : IBankTransferReceiptRepository
{
    private readonly AppDbContext _dbContext;

    public BankTransferReceiptRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<BankTransferReceipt?> GetWithPaymentAsync(int receiptId, CancellationToken cancellationToken = default) =>
        _dbContext.BankTransferReceipts.Include(receipt => receipt.MedicinePayment).ThenInclude(payment => payment!.Items)
            .SingleOrDefaultAsync(receipt => receipt.Id == receiptId, cancellationToken);

    public Task<List<BankTransferReceipt>> ListAsync(BankTransferReceiptStatus? status, CancellationToken cancellationToken = default) =>
        _dbContext.BankTransferReceipts.AsNoTracking().Include(receipt => receipt.MedicinePayment).ThenInclude(payment => payment!.Items)
            .Where(receipt => status == null || receipt.Status == status)
            .OrderByDescending(receipt => receipt.UploadedAtUtc).ThenByDescending(receipt => receipt.Id)
            .ToListAsync(cancellationToken);

    public Task<List<EventOutbox>> GetOutboxEntriesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default) =>
        _dbContext.EventOutboxEntries.AsNoTracking().Where(entry => eventIds.Contains(entry.EventId)).ToListAsync(cancellationToken);

    public async Task<HashSet<Guid>> GetNotifiedEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default) =>
        (await _dbContext.PatientNotifications.AsNoTracking().Where(notification => eventIds.Contains(notification.EventId))
            .Select(notification => notification.EventId).ToListAsync(cancellationToken)).ToHashSet();

    public Task<BankTransferReceipt?> FindVerifiedByReferenceAsync(string reference, int excludingReceiptId, CancellationToken cancellationToken = default) =>
        _dbContext.BankTransferReceipts.AsNoTracking().Include(receipt => receipt.MedicinePayment)
            .Where(receipt => receipt.Id != excludingReceiptId && receipt.Status == BankTransferReceiptStatus.Verified
                && receipt.TransactionReference == reference)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<PaymentStaffNotification>> ListNotificationsAsync(int take, CancellationToken cancellationToken = default) =>
        _dbContext.PaymentStaffNotifications.AsNoTracking()
            .OrderByDescending(notification => notification.CreatedAtUtc).ThenByDescending(notification => notification.Id)
            .Take(take).ToListAsync(cancellationToken);

    public Task<PaymentStaffNotification?> GetNotificationAsync(int notificationId, CancellationToken cancellationToken = default) =>
        _dbContext.PaymentStaffNotifications.SingleOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);

    public void Add(BankTransferReceipt receipt) => _dbContext.BankTransferReceipts.Add(receipt);

    public void AddOutboxEntry(EventOutbox entry) => _dbContext.EventOutboxEntries.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);

    public void DiscardChanges() => _dbContext.ChangeTracker.Clear();
}
