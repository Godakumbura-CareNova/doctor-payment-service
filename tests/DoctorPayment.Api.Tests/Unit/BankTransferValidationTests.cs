using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using DoctorPayment.Api.Clients;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.Controllers;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Events;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>
/// GCN-42: staff validate bank transfer transactions. Approval checks the transaction against the order in the pharmacy
/// (and any receipt details staff give) and rejects a mismatch with the reason; rejection records the reason; the result is published on Kafka (next to the GCN-40 events) and the patient
/// is notified; an unviewable receipt shows an error; only pharmacy staff may validate. Real services and repositories
/// run against the in-memory provider; the pharmacy-order service and file storage are fakes.
/// </summary>
public sealed class BankTransferValidationTests
{
    private const string PatientToken = "Bearer patient-token";
    private const string StaffToken = "Bearer staff-token";
    private const string CustomerName = "Nimal Perera";
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };

    // Scenario 1: a transaction that matches its order is approved, the payment becomes Paid, the reviewer is recorded,
    // and both the GCN-40 "verified" event and the GCN-42 "approved" event are queued. Receipt details are optional and
    // are recorded when given.
    [Fact]
    public async Task Approve_ValidTransaction_MarksPaidAndQueuesEvents()
    {
        await using var dbContext = CreateDbContext();
        var receiptId = await UploadReceiptAsync(dbContext);

        var approved = await CreateService(dbContext).ApproveAsync(receiptId, StaffId, null, StaffToken);

        Assert.Equal(("Verified", "Paid"), (approved!.Status, approved.PaymentStatus));
        Assert.Null(approved.TransactionReference);
        var receipt = await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync();
        Assert.Equal((StaffId, (int?)null), (receipt.ReviewedBy, receipt.PendingPaymentId));
        var payment = await dbContext.MedicinePayments.AsNoTracking().Include(item => item.StatusHistory).SingleAsync();
        Assert.Equal(MedicinePaymentStatus.Paid, payment.Status);
        Assert.Equal(("PharmacyStaff", MedicinePaymentStatus.Paid), (payment.StatusHistory.Last().ChangedByRole, payment.StatusHistory.Last().ToStatus));

        var events = await dbContext.EventOutboxEntries.AsNoTracking().ToListAsync();
        Assert.Contains(events, entry => entry.Topic == EventTypes.PaymentTopic && entry.EventType == EventTypes.BankReceiptVerified);
        var validation = Assert.Single(events, entry => entry.Topic == EventTypes.BankTransferValidationTopic);
        Assert.Equal((EventTypes.BankTransferApproved, receipt.ValidationEventId), (validation.EventType, (Guid?)validation.EventId));
        var data = EventData(validation);
        Assert.Equal((PatientId, "ORD-7", 450m, "Paid"), (data.PatientId, data.OrderNumber, data.Amount, data.PaymentStatus));

        var withDetailsId = await UploadReceiptAsync(dbContext, orderId: 8);
        var withDetails = await CreateService(dbContext).ApproveAsync(withDetailsId, StaffId, Details(" trx-1001 ", 450m), StaffToken);
        Assert.Equal(("TRX-1001", 450m, Now.Date), (withDetails!.TransactionReference, withDetails.TransferredAmount, withDetails.TransferDateUtc));
    }

    // If something goes wrong: an order whose total in the pharmacy no longer matches the amount paid prevents approval,
    // and the transaction is rejected with the reason; so is a transferred amount (when given) that differs from the
    // order total. The rejected transaction cannot be approved afterwards, and the patient can upload a new receipt.
    [Fact]
    public async Task Approve_AmountMismatch_IsPreventedAndRejected()
    {
        await using var dbContext = CreateDbContext();
        var receiptId = await UploadReceiptAsync(dbContext);
        var service = CreateService(dbContext, pharmacy: Pharmacy(total: 600m));

        var mismatch = await Assert.ThrowsAsync<BankTransferMismatchException>(() => service.ApproveAsync(receiptId, StaffId, null, StaffToken));

        const string totalReason = "The order total in the pharmacy (LKR 600.00) does not match the amount paid by bank transfer (LKR 450.00).";
        Assert.Contains(totalReason, mismatch.Message);
        Assert.Equal(("Rejected", "ReceiptRejected"), (mismatch.Receipt.Status, mismatch.Receipt.PaymentStatus));
        var receipt = await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync();
        Assert.Equal((BankTransferReceiptStatus.Rejected, StaffId), (receipt.Status, receipt.ReviewedBy));
        Assert.Equal("The transaction does not match the order. " + totalReason, receipt.RejectionReason);
        Assert.Equal(MedicinePaymentStatus.ReceiptRejected, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
        var events = await dbContext.EventOutboxEntries.AsNoTracking().ToListAsync();
        Assert.Contains(events, entry => entry.EventType == EventTypes.BankReceiptRejected);
        Assert.Equal(EventTypes.BankTransferRejected, Assert.Single(events, entry => entry.Topic == EventTypes.BankTransferValidationTopic).EventType);

        var typedAmountId = await UploadReceiptAsync(dbContext, orderId: 8);
        var typed = await Assert.ThrowsAsync<BankTransferMismatchException>(() =>
            CreateService(dbContext).ApproveAsync(typedAmountId, StaffId, Details("TRX-2002", 400m), StaffToken));
        Assert.Contains("The transferred amount (LKR 400.00) does not match the order total (LKR 450.00).", typed.Message);

        var again = await Assert.ThrowsAsync<ReceiptReviewConflictException>(() => CreateService(dbContext).ApproveAsync(receiptId, StaffId, null, StaffToken));
        Assert.Equal("This receipt has already been rejected.", again.Message);
        var retry = await CreateService(dbContext).UploadAsync(PatientId, CustomerName, 7,
            new FormFile(new MemoryStream(JpegBytes), 0, JpegBytes.Length, "file", "clear.jpg"), PatientToken);
        Assert.Equal("ReceiptUnderVerification", retry.Payment.Status);
    }

    // If something goes wrong: a reference already used for another order, and a transfer dated before the order,
    // are both reported and the transaction is rejected.
    [Fact]
    public async Task Approve_ReusedReferenceAndEarlyDate_AreRejected()
    {
        await using var dbContext = CreateDbContext();
        var firstId = await UploadReceiptAsync(dbContext, orderId: 7);
        var service = CreateService(dbContext);
        await service.ApproveAsync(firstId, StaffId, Details("TRX-3003", 450m), StaffToken);
        var secondId = await UploadReceiptAsync(dbContext, orderId: 8);

        var mismatch = await Assert.ThrowsAsync<BankTransferMismatchException>(() =>
            service.ApproveAsync(secondId, StaffId, Details("trx-3003", 450m, Now.AddDays(-3)), StaffToken));

        Assert.Contains("Transaction reference TRX-3003 was already used to pay order ORD-7.", mismatch.Message);
        Assert.Contains("before bank transfer was chosen", mismatch.Message);
        var second = await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync(receipt => receipt.Id == secondId);
        Assert.Equal(BankTransferReceiptStatus.Rejected, second.Status);
        Assert.Contains("already used to pay order ORD-7", second.RejectionReason);
    }

    // If something goes wrong: an order the pharmacy no longer has active cannot be paid for, so the transfer is rejected;
    // when the pharmacy cannot be reached nothing changes and staff can try again.
    [Fact]
    public async Task Approve_OrderNotActive_IsRejected_AndUnavailablePharmacyChangesNothing()
    {
        await using var inactiveContext = CreateDbContext();
        var inactiveId = await UploadReceiptAsync(inactiveContext);
        var inactive = await Assert.ThrowsAsync<BankTransferMismatchException>(() =>
            CreateService(inactiveContext, pharmacy: Pharmacy(activeStatus: null)).ApproveAsync(inactiveId, StaffId, null, StaffToken));
        Assert.Contains("no longer active in the pharmacy", inactive.Message);
        Assert.Equal(BankTransferReceiptStatus.Rejected, (await inactiveContext.BankTransferReceipts.AsNoTracking().SingleAsync()).Status);

        await using var downContext = CreateDbContext();
        var downId = await UploadReceiptAsync(downContext);
        var down = Pharmacy();
        down.Setup(client => client.GetActiveOrderAsync(It.IsAny<int>(), StaffToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderServiceUnavailableException("We could not load your order right now. Please try again."));
        await Assert.ThrowsAsync<OrderServiceUnavailableException>(() =>
            CreateService(downContext, pharmacy: down).ApproveAsync(downId, StaffId, null, StaffToken));
        Assert.Equal(BankTransferReceiptStatus.PendingVerification, (await downContext.BankTransferReceipts.AsNoTracking().SingleAsync()).Status);
        Assert.DoesNotContain(downContext.EventOutboxEntries, entry => entry.Topic == EventTypes.BankTransferValidationTopic);
    }

    // Receipt details are optional, but when staff give them they must be complete and possible; otherwise they are
    // refused without changing anything.
    [Fact]
    public async Task Approve_MissingOrInvalidDetails_AreRefusedWithoutChanges()
    {
        await using var dbContext = CreateDbContext();
        var receiptId = await UploadReceiptAsync(dbContext);
        var service = CreateService(dbContext);

        foreach (var details in new ApproveBankTransferDto[]
        {
            Details("   ", 450m),
            Details(new string('R', 101), 450m),
            new() { TransactionReference = "TRX-5005", TransferDate = Now },
            Details("TRX-5005", 0m),
            new() { TransactionReference = "TRX-5005", TransferredAmount = 450m },
            Details("TRX-5005", 450m, Now.AddDays(2))
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveAsync(receiptId, StaffId, details, StaffToken));
        }

        var receipt = await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync();
        Assert.Equal((BankTransferReceiptStatus.PendingVerification, (string?)null), (receipt.Status, receipt.TransactionReference));
        Assert.Null(await service.ApproveAsync(999, StaffId, Details("TRX-5005", 450m), StaffToken));
    }

    // Scenario 2: an unclear receipt is rejected with a reason, the payment becomes ReceiptRejected, and both the GCN-40
    // "rejected" event and the GCN-42 "rejected" event are queued.
    [Fact]
    public async Task Reject_InvalidTransaction_UpdatesStatusAndQueuesEvents()
    {
        await using var dbContext = CreateDbContext();
        var receiptId = await UploadReceiptAsync(dbContext);
        var service = CreateService(dbContext);

        var rejected = await service.RejectAsync(receiptId, StaffId, "The receipt is blurry and the amount cannot be read.");

        Assert.Equal(("Rejected", "ReceiptRejected"), (rejected!.Status, rejected.PaymentStatus));
        Assert.Equal(MedicinePaymentStatus.ReceiptRejected, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
        var events = await dbContext.EventOutboxEntries.AsNoTracking().ToListAsync();
        Assert.Contains(events, entry => entry.EventType == EventTypes.BankReceiptRejected);
        var rejectedEvent = Assert.Single(events, entry => entry.EventType == EventTypes.BankTransferRejected);
        Assert.Equal(EventTypes.BankTransferValidationTopic, rejectedEvent.Topic);
        Assert.Equal("The receipt is blurry and the amount cannot be read.", EventData(rejectedEvent).Reason);
        await Assert.ThrowsAsync<ReceiptReviewConflictException>(() => service.RejectAsync(receiptId, StaffId, "Again"));
        await Assert.ThrowsAsync<ReceiptReviewConflictException>(() => service.ApproveAsync(receiptId, StaffId, null, StaffToken));
    }

    // Kafka: the consumed "rejected" event notifies the patient once, with the reason, even when it is redelivered;
    // staff see that the patient was notified.
    [Fact]
    public async Task RejectedEvent_NotifiesPatientOnce()
    {
        await using var dbContext = CreateDbContext();
        var receiptId = await UploadReceiptAsync(dbContext);
        var service = CreateService(dbContext);
        await service.RejectAsync(receiptId, StaffId, "The reference is not in our bank statement.");
        var rejectedEvent = await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync(entry => entry.EventType == EventTypes.BankTransferRejected);
        var handler = new KafkaNotificationHandler(dbContext);

        await handler.HandleAsync(rejectedEvent.Topic, rejectedEvent.PayloadJson);
        await handler.HandleAsync(rejectedEvent.Topic, rejectedEvent.PayloadJson);

        var notification = await dbContext.PatientNotifications.AsNoTracking().SingleAsync();
        Assert.Equal((PatientId, "Your bank transfer was rejected", BankTransferNotificationStatuses.Rejected, 7),
            (notification.PatientId, notification.Title, notification.OrderStatus, notification.OrderId));
        Assert.Contains("The reference is not in our bank statement.", notification.Message);
        Assert.Contains("ORD-7", notification.Message);
        Assert.True(Assert.Single(await service.ListAsync(null)).PatientNotified);
    }

    // Kafka: the consumed "approved" event notifies the patient that the payment is confirmed and the published event
    // shows as sent; a transaction rejected for not matching its order notifies the patient with that reason.
    [Fact]
    public async Task ApprovedEvent_NotifiesPatient_AndMismatchRejectionNotifiesPatient()
    {
        await using var dbContext = CreateDbContext();
        var approvedId = await UploadReceiptAsync(dbContext, orderId: 7);
        var mismatchId = await UploadReceiptAsync(dbContext, orderId: 8);
        var service = CreateService(dbContext);
        await service.ApproveAsync(approvedId, StaffId, null, StaffToken);
        await Assert.ThrowsAsync<BankTransferMismatchException>(() => service.ApproveAsync(mismatchId, StaffId, Details("TRX-8009", 1m), StaffToken));
        var producer = new Mock<IKafkaEventProducer>();
        await OutboxPublisherService.PublishPendingEventsAsync(dbContext, producer.Object, 3, NullLogger.Instance, CancellationToken.None);
        var handler = new KafkaNotificationHandler(dbContext);
        foreach (var entry in await dbContext.EventOutboxEntries.AsNoTracking().Where(item => item.Topic == EventTypes.BankTransferValidationTopic).ToListAsync())
        {
            await handler.HandleAsync(entry.Topic, entry.PayloadJson);
        }

        var approved = await dbContext.PatientNotifications.AsNoTracking().SingleAsync(item => item.OrderId == 7);
        Assert.Equal(("Your bank transfer was approved", BankTransferNotificationStatuses.Approved), (approved.Title, approved.OrderStatus));
        Assert.Contains("Your payment is confirmed", approved.Message);
        var rejected = await dbContext.PatientNotifications.AsNoTracking().SingleAsync(item => item.OrderId == 8);
        Assert.Equal(("Your bank transfer was rejected", BankTransferNotificationStatuses.Rejected), (rejected.Title, rejected.OrderStatus));
        Assert.Contains("does not match the order total", rejected.Message);
        Assert.Empty(dbContext.PaymentStaffNotifications.Where(item => item.OrderId == 8 && item.NotificationType != EventTypes.BankReceiptUploaded));
        var listed = (await service.ListAsync(BankTransferReceiptStatus.Verified)).Single();
        Assert.Equal(("Published", true), (listed.ValidationEvent, listed.PatientNotified));
    }

    // If something goes wrong: a receipt whose file is missing or unreadable cannot be viewed and says so; a receipt that
    // does not exist is not found; a stored receipt opens.
    [Fact]
    public async Task OpenFile_MissingOrUnreadableFile_ShowsError()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeReceiptStorage();
        var receiptId = await UploadReceiptAsync(dbContext, storage: storage);
        var service = CreateService(dbContext, storage);

        var opened = await service.OpenFileAsync(receiptId);
        Assert.Equal(("image/jpeg", "slip.jpg"), (opened!.ContentType, opened.FileName));

        storage.FailOpen = true;
        var unreadable = await Assert.ThrowsAsync<ReceiptFileUnavailableException>(() => service.OpenFileAsync(receiptId));
        Assert.Equal(BankTransferReceiptService.ReceiptFileUnavailableMessage, unreadable.Message);

        storage.FailOpen = false;
        storage.Files.Clear();
        await Assert.ThrowsAsync<ReceiptFileUnavailableException>(() => service.OpenFileAsync(receiptId));
        Assert.Null(await service.OpenFileAsync(999));
    }

    // Scenario 3: only pharmacy staff may validate transactions. Patients, doctors, admins and signed-out users are denied
    // by the controller's policy, and a staff token without an identity changes nothing.
    [Fact]
    public async Task Authorization_OnlyPharmacyStaffCanValidate()
    {
        var authorization = new ServiceCollection().AddLogging().AddDoctorPaymentAuthorization().BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();
        async Task<bool> Can(ClaimsPrincipal user) => (await authorization.AuthorizeAsync(user, AuthorizationPolicies.PharmacyStaff)).Succeeded;

        var policy = typeof(StaffBankTransferReceiptsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.PharmacyStaff, policy?.Policy);
        Assert.DoesNotContain(typeof(StaffBankTransferReceiptsController).GetMethods(),
            method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
        Assert.True(await Can(User("PharmacyStaff", StaffId)));
        Assert.True(await Can(User("Pharmacy Staff", StaffId)));
        foreach (var user in new[] { User("Patient", PatientId), User("Doctor", Guid.NewGuid()), User("Admin", Guid.NewGuid()), new ClaimsPrincipal(new ClaimsIdentity()) })
        {
            Assert.False(await Can(user));
        }

        await using var dbContext = CreateDbContext();
        var receiptId = await UploadReceiptAsync(dbContext);
        var controller = new StaffBankTransferReceiptsController(CreateService(dbContext))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User("PharmacyStaff", null) } }
        };
        var approve = await controller.Approve(receiptId, Details("TRX-9009", 450m), CancellationToken.None);
        var reject = await controller.Reject(receiptId, new RejectBankTransferReceiptDto { Reason = "Unclear" }, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(approve.Result);
        Assert.IsType<UnauthorizedObjectResult>(reject.Result);
        Assert.Equal(BankTransferReceiptStatus.PendingVerification, (await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync()).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(dbContext).ApproveAsync(receiptId, Guid.Empty, Details("TRX-9009", 450m), StaffToken));
    }

    private static ApproveBankTransferDto Details(string reference, decimal amount, DateTime? date = null) =>
        new() { TransactionReference = reference, TransferredAmount = amount, TransferDate = date ?? Now };

    private static BankTransferValidationEventData EventData(EventOutbox entry) =>
        JsonSerializer.Deserialize<PaymentEvent>(entry.PayloadJson)!.Data.Deserialize<BankTransferValidationEventData>()!;

    private static ClaimsPrincipal User(string role, Guid? id)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (id is { } value) claims.Add(new Claim(ClaimTypes.NameIdentifier, value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>Order ORD-{id}: 2 x Iodex and 1 x Panadol for LKR 450.</summary>
    private static PharmacyOrderSnapshot Order(int id) =>
        new(id, $"ORD-{id}", "Pending", 450m, new List<PharmacyOrderLine> { new("Iodex", 2, 200m), new("Panadol", 1, 50m) });

    /// <summary>
    /// The pharmacy-order service: every order exists and is active (Pending) for LKR 450, unless <paramref name="activeStatus"/>
    /// (null: not active) or <paramref name="total"/> say otherwise.
    /// </summary>
    private static Mock<IPharmacyOrderClient> Pharmacy(string? activeStatus = "Pending", decimal total = 450m)
    {
        var pharmacy = new Mock<IPharmacyOrderClient>();
        pharmacy.Setup(client => client.GetOrderAsync(It.IsAny<int>(), PatientToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, string? _, CancellationToken _) => Order(id));
        pharmacy.Setup(client => client.GetActiveOrderAsync(It.IsAny<int>(), StaffToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, string? _, CancellationToken _) => activeStatus is null ? null : new StaffOrderSnapshot(id, $"ORD-{id}", activeStatus, total));
        return pharmacy;
    }

    private static MedicinePaymentSettings PaymentSettings() => new()
    {
        CashMaxAmount = 25_000m,
        BankTransfer = new BankTransferSettings { BankName = "Test Bank", Branch = "Colombo", AccountName = "CareNova Pharmacy", AccountNumber = "123-456" }
    };

    /// <summary>The patient chooses bank transfer for the order and uploads a receipt (GCN-39 and GCN-40); returns the receipt id.</summary>
    private static async Task<int> UploadReceiptAsync(AppDbContext dbContext, int orderId = 7, FakeReceiptStorage? storage = null)
    {
        var pharmacy = Pharmacy();
        var payments = new MedicinePaymentService(new MedicinePaymentRepository(dbContext), pharmacy.Object,
            Options.Create(PaymentSettings()), new FakeTimeProvider(new DateTimeOffset(Now)));
        await payments.SelectAsync(PatientId, orderId, new SelectPaymentMethodRequestDto { Method = "BankTransfer" }, PatientToken);
        var file = new FormFile(new MemoryStream(JpegBytes), 0, JpegBytes.Length, "file", "slip.jpg");
        await CreateService(dbContext, storage, pharmacy).UploadAsync(PatientId, CustomerName, orderId, file, PatientToken);
        return (await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync(receipt => receipt.OrderId == orderId)).Id;
    }

    private static BankTransferReceiptService CreateService(AppDbContext dbContext, FakeReceiptStorage? storage = null,
        Mock<IPharmacyOrderClient>? pharmacy = null) =>
        new(new BankTransferReceiptRepository(dbContext), new MedicinePaymentRepository(dbContext), (pharmacy ?? Pharmacy()).Object,
            storage ?? new FakeReceiptStorage(), Options.Create(PaymentSettings()), Options.Create(new ReceiptStorageSettings()),
            Options.Create(new KafkaSettings { PublishRetryLimit = 3 }), new FakeTimeProvider(new DateTimeOffset(Now)),
            NullLogger<BankTransferReceiptService>.Instance);

    private static AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"BankTransferValidation-{Guid.NewGuid():N}").Options);

    /// <summary>Keeps receipts in memory; <see cref="FailOpen"/> simulates an unreadable file.</summary>
    private sealed class FakeReceiptStorage : IReceiptFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public bool FailOpen { get; set; }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var name = $"{Guid.NewGuid():N}{extension}";
            Files[name] = copy.ToArray();
            return name;
        }

        public Stream? Open(string storedFileName)
        {
            if (FailOpen) throw new IOException("The file is damaged.");
            return Files.TryGetValue(storedFileName, out var content) ? new MemoryStream(content) : null;
        }

        public void Delete(string storedFileName) => Files.Remove(storedFileName);
    }
}
