using System.Text.Json;
using DoctorPayment.Api.Clients;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Events;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>
/// GCN-40: uploading a bank transfer receipt for a medicine order, the Kafka verification event, and staff verification.
/// Runs the real services and repositories against the in-memory provider; the pharmacy-order service and Kafka are fakes.
/// </summary>
public sealed class BankTransferReceiptTests
{
    private const string Token = "Bearer patient-token";
    private const string CustomerName = "Nimal Perera";
    private static readonly DateTime Now = new(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
    private static readonly byte[] PdfBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x25 };

    // Scenario 1: a valid JPG is stored, tied to the order's payment, and the order stays Pending (not confirmed).
    [Fact]
    public async Task Upload_ValidJpg_StoresReceiptAndKeepsOrderPending()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeReceiptStorage();
        await ChooseBankTransferAsync(dbContext);

        var result = await CreateService(dbContext, storage).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);

        Assert.Equal("Pending", result.OrderStatus);
        Assert.Equal(BankTransferReceiptService.UploadedMessage, result.Message);
        Assert.Equal(("ReceiptUnderVerification", "PendingVerification", "slip.jpg"),
            (result.Payment.Status, result.Payment.Receipt!.Status, result.Payment.Receipt.FileName));
        var receipt = await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync();
        var payment = await dbContext.MedicinePayments.AsNoTracking().SingleAsync();
        Assert.Equal((payment.Id, 7, "image/jpeg"), (receipt.MedicinePaymentId, receipt.OrderId, receipt.ContentType));
        Assert.True(storage.Files.ContainsKey(receipt.StoredFileName));
        Assert.NotEqual(MedicinePaymentStatus.Paid, payment.Status);
    }

    // Scenario 1: PNG and JPEG receipts are accepted as well as JPG.
    [Fact]
    public async Task Upload_PngAndJpeg_AreAccepted()
    {
        await using var pngContext = CreateDbContext();
        await ChooseBankTransferAsync(pngContext);
        var png = await CreateService(pngContext).UploadAsync(PatientId, CustomerName, 7, File("slip.PNG", PngBytes), Token);
        Assert.Equal("image/png", png.Payment.Receipt!.ContentType);

        await using var jpegContext = CreateDbContext();
        await ChooseBankTransferAsync(jpegContext);
        var jpeg = await CreateService(jpegContext).UploadAsync(PatientId, CustomerName, 7, File("slip.jpeg", JpegBytes), Token);
        Assert.Equal("image/jpeg", jpeg.Payment.Receipt!.ContentType);
    }

    // Scenario 2: the upload queues a Kafka event with the order details, customer name and a verification message.
    [Fact]
    public async Task Upload_QueuesKafkaEventWithOrderAndCustomerDetails()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);

        await CreateService(dbContext).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);

        var outbox = await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync();
        Assert.Equal((EventTypes.PaymentTopic, EventTypes.BankReceiptUploaded, "7"), (outbox.Topic, outbox.EventType, outbox.EventKey));
        Assert.Null(outbox.PublishedAtUtc);
        var paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(outbox.PayloadJson)!;
        var data = paymentEvent.Data.Deserialize<BankReceiptEventData>()!;
        Assert.Equal((7, "ORD-7", 450m, CustomerName), (data.OrderId, data.OrderNumber, data.Amount, data.CustomerName));
        Assert.Equal("Bank transfer receipt uploaded for verification.", data.Message);
        Assert.Equal(new[] { new BankReceiptEventItem("Iodex", 2), new BankReceiptEventItem("Panadol", 1) }, data.Items);
        Assert.Equal(outbox.EventId, (await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync()).UploadEventId);
    }

    // Scenario 2: the verification process turns the consumed event into one staff notification, even if redelivered.
    [Fact]
    public async Task ConsumedUploadEvent_CreatesOneStaffNotification()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);
        await CreateService(dbContext).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);
        var outbox = await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync();
        var handler = new KafkaNotificationHandler(dbContext);

        await handler.HandleAsync(outbox.Topic, outbox.PayloadJson);
        await handler.HandleAsync(outbox.Topic, outbox.PayloadJson);

        var notification = await dbContext.PaymentStaffNotifications.AsNoTracking().SingleAsync();
        Assert.Equal("Bank receipt uploaded for verification", notification.Title);
        Assert.Contains(CustomerName, notification.Message);
        Assert.Contains("ORD-7", notification.Message);
        Assert.Contains("2 x Iodex, 1 x Panadol", notification.Message);
        Assert.Equal((7, false), (notification.OrderId, notification.IsRead));
    }

    // Scenario 3: while the receipt is being verified, the payment is not confirmed and cannot be changed or re-uploaded.
    [Fact]
    public async Task WhileVerifying_PaymentStaysUnconfirmedAndLocked()
    {
        await using var dbContext = CreateDbContext();
        var payments = await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);
        await service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);

        Assert.Equal("ReceiptUnderVerification", (await payments.GetSelectedAsync(PatientId, 7))!.Status);
        await Assert.ThrowsAsync<MedicinePaymentConflictException>(() =>
            payments.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "Cash" }, Token));
        await Assert.ThrowsAsync<ReceiptReviewConflictException>(() =>
            service.UploadAsync(PatientId, CustomerName, 7, File("again.jpg", JpegBytes), Token));
        Assert.Single(dbContext.BankTransferReceipts);
    }

    // Scenario 4: only a staff verification marks the payment Paid, and it is announced on Kafka.
    [Fact]
    public async Task Verify_MarksPaymentPaidAndQueuesVerifiedEvent()
    {
        await using var dbContext = CreateDbContext();
        var payments = await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);
        await service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);
        var receiptId = (await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync()).Id;

        var verified = await service.VerifyAsync(receiptId, StaffId);

        Assert.Equal(("Verified", "Paid"), (verified!.Status, verified.PaymentStatus));
        var payment = (await payments.GetSelectedAsync(PatientId, 7))!;
        Assert.Equal(("Paid", "Verified"), (payment.Status, payment.Receipt!.Status));
        Assert.Contains(dbContext.EventOutboxEntries, entry => entry.EventType == EventTypes.BankReceiptVerified);
        Assert.Null((await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync()).PendingPaymentId);
    }

    // Receipt validation fails: the payment is not confirmed and the patient can upload a new receipt.
    [Fact]
    public async Task Reject_KeepsPaymentUnconfirmedAndAllowsANewUpload()
    {
        await using var dbContext = CreateDbContext();
        var payments = await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);
        await service.UploadAsync(PatientId, CustomerName, 7, File("blurry.jpg", JpegBytes), Token);
        var firstId = (await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync()).Id;

        var rejected = await service.RejectAsync(firstId, StaffId, "  The amount is not readable.  ");
        Assert.Equal(("Rejected", "ReceiptRejected", "The amount is not readable."), (rejected!.Status, rejected.PaymentStatus, rejected.RejectionReason));
        Assert.Equal("ReceiptRejected", (await payments.GetSelectedAsync(PatientId, 7))!.Status);

        var retry = await service.UploadAsync(PatientId, CustomerName, 7, File("clear.png", PngBytes), Token);
        Assert.Equal(("ReceiptUnderVerification", "clear.png"), (retry.Payment.Status, retry.Payment.Receipt!.FileName));
        Assert.Equal(2, await dbContext.BankTransferReceipts.CountAsync());
    }

    // A rejection needs a reason, and a receipt that was already reviewed cannot be reviewed again.
    [Fact]
    public async Task Review_RequiresReasonAndCannotRepeat()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);
        await service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);
        var receiptId = (await dbContext.BankTransferReceipts.AsNoTracking().SingleAsync()).Id;

        await Assert.ThrowsAsync<ArgumentException>(() => service.RejectAsync(receiptId, StaffId, "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RejectAsync(receiptId, StaffId, new string('x', 501)));
        Assert.NotNull(await service.VerifyAsync(receiptId, StaffId));

        var again = await Assert.ThrowsAsync<ReceiptReviewConflictException>(() => service.RejectAsync(receiptId, StaffId, "Wrong amount"));
        Assert.Equal("This receipt has already been verified.", again.Message);
        Assert.Null(await service.VerifyAsync(999, StaffId));
    }

    // Scenario 5: a file that is not JPG, JPEG or PNG is refused with a format message and nothing is stored.
    [Fact]
    public async Task Upload_UnsupportedFormat_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeReceiptStorage();
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext, storage);

        foreach (var name in new[] { "slip.pdf", "slip.gif", "slip.webp", "slip" })
        {
            var exception = await Assert.ThrowsAsync<InvalidReceiptFileException>(() =>
                service.UploadAsync(PatientId, CustomerName, 7, File(name, PdfBytes), Token));
            Assert.Equal(BankTransferReceiptService.UnsupportedFormatMessage, exception.Message);
        }

        Assert.Empty(storage.Files);
        Assert.Empty(dbContext.BankTransferReceipts);
    }

    // Scenario 5: a file renamed to .png or .jpg is checked by its content, not trusted by its name.
    [Fact]
    public async Task Upload_RenamedNonImage_IsRejectedByContent()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);

        var renamedPdf = await Assert.ThrowsAsync<InvalidReceiptFileException>(() =>
            service.UploadAsync(PatientId, CustomerName, 7, File("slip.png", PdfBytes), Token));
        var pngCalledJpg = await Assert.ThrowsAsync<InvalidReceiptFileException>(() =>
            service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", PngBytes), Token));

        Assert.Equal(BankTransferReceiptService.InvalidImageMessage, renamedPdf.Message);
        Assert.Equal(BankTransferReceiptService.InvalidImageMessage, pngCalledJpg.Message);
        Assert.Empty(dbContext.BankTransferReceipts);
    }

    // Scenario 6: a receipt over 5 MB is refused with a size message; exactly 5 MB is allowed.
    [Fact]
    public async Task Upload_Over5Mb_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);
        const int fiveMb = 5 * 1024 * 1024;

        var exception = await Assert.ThrowsAsync<InvalidReceiptFileException>(() =>
            service.UploadAsync(PatientId, CustomerName, 7, File("big.jpg", Sized(JpegBytes, fiveMb + 1)), Token));
        Assert.Equal("The receipt is larger than 5 MB. Please upload a file of 5 MB or less.", exception.Message);
        Assert.Empty(dbContext.BankTransferReceipts);

        var exact = await service.UploadAsync(PatientId, CustomerName, 7, File("exact.jpg", Sized(JpegBytes, fiveMb)), Token);
        Assert.Equal(fiveMb, exact.Payment.Receipt!.SizeBytes);
    }

    // Scenario 7: submitting without a file (or with an empty one) asks the patient to select a receipt.
    [Fact]
    public async Task Upload_NoFile_AsksForAReceipt()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);

        var missing = await Assert.ThrowsAsync<InvalidReceiptFileException>(() => service.UploadAsync(PatientId, CustomerName, 7, null, Token));
        var empty = await Assert.ThrowsAsync<InvalidReceiptFileException>(() =>
            service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", Array.Empty<byte>()), Token));

        Assert.Equal(BankTransferReceiptService.MissingFileMessage, missing.Message);
        Assert.Equal(BankTransferReceiptService.MissingFileMessage, empty.Message);
        Assert.Empty(dbContext.BankTransferReceipts);
    }

    // Scenario 8: when storing the file fails, nothing is recorded and the patient can retry successfully.
    [Fact]
    public async Task Upload_StorageFailure_CanBeRetried()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeReceiptStorage { FailNextSave = true };
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext, storage);

        var failure = await Assert.ThrowsAsync<ReceiptUploadFailedException>(() =>
            service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token));
        Assert.Equal(BankTransferReceiptService.UploadFailedMessage, failure.Message);
        Assert.Empty(dbContext.BankTransferReceipts);
        Assert.Empty(dbContext.EventOutboxEntries);
        Assert.Equal(MedicinePaymentStatus.AwaitingBankTransfer, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);

        var retry = await service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);
        Assert.Equal("ReceiptUnderVerification", retry.Payment.Status);
        Assert.Single(storage.Files);
    }

    // Scenario 9: a receipt that cannot be tied to the order's bank transfer is refused and nothing is stored.
    [Fact]
    public async Task Upload_CannotBeAssociated_IsRejected()
    {
        await using var noPaymentContext = CreateDbContext();
        var noPayment = await Assert.ThrowsAsync<ReceiptAssociationException>(() =>
            CreateService(noPaymentContext).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token));
        Assert.StartsWith("Bank transfer is not the payment method for this order.", noPayment.Message);

        await using var cashContext = CreateDbContext();
        await ChooseAsync(cashContext, "Cash");
        await Assert.ThrowsAsync<ReceiptAssociationException>(() =>
            CreateService(cashContext).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token));

        await using var changedContext = CreateDbContext();
        await ChooseBankTransferAsync(changedContext);
        var storage = new FakeReceiptStorage();
        var changed = await Assert.ThrowsAsync<ReceiptAssociationException>(() =>
            CreateService(changedContext, storage, Order(7, 999m)).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token));
        Assert.Contains("cannot be matched", changed.Message);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateService(changedContext, storage, null).UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token));
        Assert.Empty(storage.Files);
        Assert.Empty(changedContext.BankTransferReceipts);
    }

    // Scenario 10: when Kafka publishing fails, the failure is recorded and retried, and the payment is never treated as paid.
    [Fact]
    public async Task KafkaPublishFailure_IsRecordedAndPaymentIsNotConfirmed()
    {
        await using var dbContext = CreateDbContext();
        await ChooseBankTransferAsync(dbContext);
        var service = CreateService(dbContext);
        await service.UploadAsync(PatientId, CustomerName, 7, File("slip.jpg", JpegBytes), Token);
        var producer = new Mock<IKafkaEventProducer>();
        producer.Setup(kafka => kafka.PublishAsync(It.IsAny<EventOutbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Broker not available"));

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await OutboxPublisherService.PublishPendingEventsAsync(dbContext, producer.Object, 3, NullLogger.Instance, CancellationToken.None);
        }

        var outbox = await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync();
        Assert.Equal((3, "Broker not available"), (outbox.PublishAttempts, outbox.LastError));
        Assert.Null(outbox.PublishedAtUtc);
        producer.Verify(kafka => kafka.PublishAsync(It.IsAny<EventOutbox>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        Assert.Equal(MedicinePaymentStatus.ReceiptUnderVerification, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
        var listed = Assert.Single(await service.ListAsync(BankTransferReceiptStatus.PendingVerification));
        Assert.Equal(("Failed", "ReceiptUnderVerification", CustomerName), (listed.VerificationEvent, listed.PaymentStatus, listed.CustomerName));
    }

    // 2 x Iodex and 1 x Panadol (LKR 50), adding up to the total.
    private static PharmacyOrderSnapshot Order(int id, decimal total) =>
        new(id, $"ORD-{id}", "Pending", total, new List<PharmacyOrderLine> { new("Iodex", 2, (total - 50m) / 2), new("Panadol", 1, 50m) });

    private static MedicinePaymentSettings PaymentSettings() => new()
    {
        CashMaxAmount = 25_000m,
        BankTransfer = new BankTransferSettings { BankName = "Test Bank", Branch = "Colombo", AccountName = "CareNova Pharmacy", AccountNumber = "123-456" }
    };

    private static IPharmacyOrderClient Orders(PharmacyOrderSnapshot? order)
    {
        var orders = new Mock<IPharmacyOrderClient>();
        orders.Setup(client => client.GetOrderAsync(It.IsAny<int>(), Token, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        return orders.Object;
    }

    private static Task<MedicinePaymentService> ChooseBankTransferAsync(AppDbContext dbContext) => ChooseAsync(dbContext, "BankTransfer");

    private static async Task<MedicinePaymentService> ChooseAsync(AppDbContext dbContext, string method)
    {
        var service = new MedicinePaymentService(new MedicinePaymentRepository(dbContext), Orders(Order(7, 450m)),
            Options.Create(PaymentSettings()), new FakeTimeProvider(new DateTimeOffset(Now)));
        await service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = method }, Token);
        return service;
    }

    private static BankTransferReceiptService CreateService(AppDbContext dbContext, FakeReceiptStorage? storage = null) =>
        CreateService(dbContext, storage ?? new FakeReceiptStorage(), Order(7, 450m));

    private static BankTransferReceiptService CreateService(AppDbContext dbContext, FakeReceiptStorage storage, PharmacyOrderSnapshot? order) =>
        new(new BankTransferReceiptRepository(dbContext), new MedicinePaymentRepository(dbContext), Orders(order), storage,
            Options.Create(PaymentSettings()), Options.Create(new ReceiptStorageSettings()), Options.Create(new KafkaSettings { PublishRetryLimit = 3 }),
            new FakeTimeProvider(new DateTimeOffset(Now)), NullLogger<BankTransferReceiptService>.Instance);

    private static IFormFile File(string fileName, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName);

    private static byte[] Sized(byte[] header, int length)
    {
        var content = new byte[length];
        header.CopyTo(content, 0);
        return content;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BankTransferReceipts-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Keeps receipts in memory; <see cref="FailNextSave"/> simulates a disk failure once.</summary>
    private sealed class FakeReceiptStorage : IReceiptFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public bool FailNextSave { get; set; }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Disk unavailable");
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var name = $"{Guid.NewGuid():N}{extension}";
            Files[name] = copy.ToArray();
            return name;
        }

        public Stream? Open(string storedFileName) =>
            Files.TryGetValue(storedFileName, out var content) ? new MemoryStream(content) : null;

        public void Delete(string storedFileName) => Files.Remove(storedFileName);
    }
}
