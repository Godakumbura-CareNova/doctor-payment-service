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
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>
/// GCN-41: paying a medicine order online through the PayHere sandbox. Real services, repositories and the real PayHere
/// gateway (checkout signing and result signature checks) run against the in-memory provider with a test merchant; the
/// pharmacy-order service is a mock. PayHere's notify calls are simulated by signing results the way PayHere does.
/// </summary>
public sealed class OnlinePaymentTests
{
    private const string Token = "Bearer patient-token";
    private const string Origin = "https://carenova.example";
    private const string MerchantId = "1221149";
    private const string MerchantSecret = "sandbox-merchant-secret";
    private const string NotifyUrl = "https://api.carenova.example/doctor-payment/api/payments/payhere/notify";
    private static readonly DateTime Now = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.NewGuid();

    // Starting an eligible order's payment returns PayHere's checkout signed for the saved total (the hash PayHere
    // checks), with the patient's billing details; starting never marks anything paid.
    [Fact]
    public async Task Start_EligibleOrder_ReturnsSignedCheckoutForOrderTotal()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");

        var session = await CreateService(dbContext).StartAsync(PatientId, 7, Billing(phone: "+94 77 123 4567"), Token, Origin);

        Assert.Equal((true, MerchantId, "ORD-7-1", "450.00", "LKR"), (session.Sandbox, session.MerchantId, session.GatewayOrderId, session.Amount, session.Currency));
        var secretHash = PayHerePaymentGateway.Md5(MerchantSecret);
        Assert.Equal(PayHerePaymentGateway.Md5(MerchantId + "ORD-7-1" + "450.00" + "LKR" + secretHash), session.Hash);
        Assert.DoesNotContain(MerchantSecret, JsonSerializer.Serialize(session));
        Assert.Equal((NotifyUrl, $"{Origin}/patient/orders/7/payment/online"), (session.NotifyUrl, session.ReturnUrl));
        Assert.Equal(("Nimal", "Perera", "0771234567", "Sri Lanka"), (session.FirstName, session.LastName, session.Phone, session.Country));
        var attempt = await dbContext.OnlinePaymentAttempts.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentAttemptStatus.Pending, "ORD-7-1", true), (attempt.Status, attempt.GatewayReference, attempt.IsTest));
        Assert.Equal(MedicinePaymentStatus.AwaitingOnlinePayment, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
    }

    // If something goes wrong: invalid order information prevents payment from starting (rejected, collected or changed
    // order; no order; online not chosen), with a message, and no try is created.
    [Fact]
    public async Task Start_InvalidOrderInformation_IsPrevented()
    {
        foreach (var (order, expected) in new (PharmacyOrderSnapshot?, Type)[]
        {
            (Order(7, 450m, "Rejected"), typeof(InvalidMedicineOrderException)),
            (Order(7, 450m, "Collected"), typeof(InvalidMedicineOrderException)),
            (Order(7, 999m), typeof(InvalidMedicineOrderException)),
            (null, typeof(KeyNotFoundException))
        })
        {
            await using var dbContext = CreateDbContext();
            await ChooseAsync(dbContext, "Online");
            var exception = await Record.ExceptionAsync(() => CreateService(dbContext, Pharmacy(order)).StartAsync(PatientId, 7, Billing(), Token, Origin));
            Assert.IsType(expected, exception);
            Assert.Empty(dbContext.OnlinePaymentAttempts);
        }

        await using var cashContext = CreateDbContext();
        await ChooseAsync(cashContext, "Cash");
        var cash = await Assert.ThrowsAsync<PaymentMethodUnavailableException>(() => CreateService(cashContext).StartAsync(PatientId, 7, Billing(), Token, Origin));
        Assert.StartsWith("Online payment is not the payment method", cash.Message);
        await using var noPaymentContext = CreateDbContext();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(noPaymentContext).StartAsync(PatientId, 7, Billing(), Token, Origin));
    }

    // If something goes wrong: invalid payment information gets a validation message, and nothing is started.
    [Fact]
    public async Task Start_InvalidPaymentInformation_ShowsValidationMessage()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var service = CreateService(dbContext);

        foreach (var (billing, message) in new (StartOnlinePaymentRequestDto?, string)[]
        {
            (null, "Enter your first name."),
            (Billing(firstName: "  "), "Enter your first name."),
            (Billing(firstName: "N1mal"), "Your first name can only contain letters, spaces, dots, apostrophes and hyphens."),
            (Billing(email: "nimal@"), "Enter a valid email address."),
            (Billing(phone: "12345"), "Enter a valid Sri Lankan phone number, e.g. 0771234567 or +94771234567."),
            (Billing(city: ""), "Enter your city.")
        })
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(PatientId, 7, billing, Token, Origin));
            Assert.Equal(message, exception.Message);
        }
        Assert.Empty(dbContext.OnlinePaymentAttempts);
    }

    // Scenarios 1 and 2: PayHere's signed "success" (status 2) for a sandbox payment records the test transaction and marks
    // the payment Paid by the payment gateway; the patient sees the result and OnlinePaymentSucceeded is queued.
    [Fact]
    public async Task Notify_SuccessfulSandboxPayment_RecordsTransactionAndMarksPaid()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var service = CreateService(dbContext);
        var session = await service.StartAsync(PatientId, 7, Billing(), Token, Origin);
        Assert.Equal(OnlinePaymentResultDto.Processing, (await service.CompleteAsync(PatientId, 7, session.AttemptId)).Outcome);

        Assert.True(await service.HandleNotificationAsync(Notification("ORD-7-1", "450.00", 2, method: "VISA")));

        var result = await service.CompleteAsync(PatientId, 7, session.AttemptId);
        Assert.Equal((OnlinePaymentResultDto.Succeeded, "Paid", true, "ORD-7-1"), (result.Outcome, result.Payment.Status, result.IsTest, result.TransactionReference));
        Assert.Contains("sandbox test payment", result.Message);
        var attempt = await dbContext.OnlinePaymentAttempts.AsNoTracking().SingleAsync();
        Assert.Equal((OnlinePaymentAttemptStatus.Succeeded, "320025071234", "VISA"), (attempt.Status, attempt.GatewayPaymentId, attempt.PaymentMethod));
        var payment = await dbContext.MedicinePayments.AsNoTracking().Include(item => item.StatusHistory).SingleAsync();
        Assert.Equal((MedicinePaymentStatus.Paid, OnlinePaymentService.PaymentRole), (payment.Status, payment.StatusHistory.Last().ChangedByRole));
        var outbox = await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync();
        Assert.Equal((EventTypes.OnlinePaymentTopic, EventTypes.OnlinePaymentSucceeded), (outbox.Topic, outbox.EventType));
        Assert.Equal((true, "Paid", 450m, "LKR"), (EventData(outbox).IsTest, EventData(outbox).PaymentStatus, EventData(outbox).Amount, EventData(outbox).Currency));
    }

    // Scenarios 3 and 4: a declined sandbox payment (status -2) records the failed test status with PayHere's reason,
    // informs the customer, offers a retry, queues OnlinePaymentFailed, and never marks the order paid.
    [Fact]
    public async Task Notify_DeclinedPayment_RecordsFailureAndIsNotPaid()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var service = CreateService(dbContext);
        var session = await service.StartAsync(PatientId, 7, Billing(), Token, Origin);

        await service.HandleNotificationAsync(Notification("ORD-7-1", "450.00", -2, message: "Insufficient funds"));

        var result = await service.CompleteAsync(PatientId, 7, session.AttemptId);
        Assert.Equal((OnlinePaymentResultDto.Failed, "Failed", true, "-2"), (result.Outcome, result.Payment.Status, result.CanRetry, result.FailureCode));
        Assert.Equal("Payment failed: Insufficient funds Your order has not been marked as paid.", result.Message);
        Assert.Equal((OnlinePaymentAttemptStatus.Failed, true), ((await dbContext.OnlinePaymentAttempts.AsNoTracking().SingleAsync()).Status,
            (await dbContext.OnlinePaymentAttempts.AsNoTracking().SingleAsync()).IsTest));
        Assert.NotEqual(MedicinePaymentStatus.Paid, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
        var outbox = await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync();
        Assert.Equal(EventTypes.OnlinePaymentFailed, outbox.EventType);
        Assert.Equal((true, "Failed", true, "Insufficient funds"),
            (EventData(outbox).IsTest, EventData(outbox).Result, EventData(outbox).CanRetry, EventData(outbox).FailureMessage));
    }

    // Scenario 5: when the payment service is unavailable (not set up) or the pharmacy cannot be reached, the customer gets
    // an error, no try is started and nothing is marked paid.
    [Fact]
    public async Task ServiceUnavailable_ShowsErrorAndNothingIsPaid()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var notConfigured = CreateService(dbContext, settings: Settings(merchantSecret: ""));

        var start = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => notConfigured.StartAsync(PatientId, 7, Billing(), Token, Origin));
        Assert.Equal(PayHerePaymentGateway.UnavailableMessage, start.Message);
        var status = await notConfigured.GetStatusAsync(PatientId, 7);
        Assert.Equal((false, false, PayHerePaymentGateway.UnavailableMessage), (status.GatewayAvailable, status.CanPay, status.CannotPayReason));

        var pharmacyDown = new Mock<IPharmacyOrderClient>();
        pharmacyDown.Setup(client => client.GetOrderAsync(7, Token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderServiceUnavailableException("We could not load your order right now. Please try again."));
        await Assert.ThrowsAsync<OrderServiceUnavailableException>(() => CreateService(dbContext, pharmacyDown.Object).StartAsync(PatientId, 7, Billing(), Token, Origin));

        Assert.Empty(dbContext.OnlinePaymentAttempts);
        Assert.Equal(MedicinePaymentStatus.AwaitingOnlinePayment, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
    }

    // Scenario 6: after a closed window or a declined payment (both failed tries, each with a notification) the customer
    // can try again with a new PayHere order id and succeed; once the allowed failures are used, retry is not offered.
    [Fact]
    public async Task Retry_AfterFailureOrCancel_AllowsNewAttemptUntilLimit()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var service = CreateService(dbContext);
        var closed = await service.StartAsync(PatientId, 7, Billing(), Token, Origin);
        var cancelled = await service.CancelAsync(PatientId, 7, closed.AttemptId);
        Assert.Equal((OnlinePaymentResultDto.Failed, true, OnlinePaymentService.CancelledCode), (cancelled.Outcome, cancelled.CanRetry, cancelled.FailureCode));
        Assert.Equal(EventTypes.OnlinePaymentFailed, (await dbContext.EventOutboxEntries.AsNoTracking().SingleAsync()).EventType);

        var declined = await service.StartAsync(PatientId, 7, Billing(), Token, Origin);
        await service.HandleNotificationAsync(Notification("ORD-7-2", "450.00", -2, message: "Do not honor"));
        var retry = await service.StartAsync(PatientId, 7, Billing(), Token, Origin);
        Assert.Equal((3, "ORD-7-3"), (retry.AttemptNumber, retry.GatewayOrderId));
        await service.HandleNotificationAsync(Notification("ORD-7-3", "450.00", 2));
        Assert.Equal("Paid", (await service.CompleteAsync(PatientId, 7, retry.AttemptId)).Payment.Status);
        Assert.Equal((2, 3), ((await service.GetStatusAsync(PatientId, 7)).AttemptsUsed, (await service.GetStatusAsync(PatientId, 7)).Attempts.Count));
        Assert.Equal(OnlinePaymentResultDto.Failed, (await service.CompleteAsync(PatientId, 7, declined.AttemptId)).Outcome);

        await using var limitContext = CreateDbContext();
        await ChooseAsync(limitContext, "Online");
        var limited = CreateService(limitContext, settings: Settings(maxAttempts: 2));
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var session = await limited.StartAsync(PatientId, 7, Billing(), Token, Origin);
            await limited.HandleNotificationAsync(Notification($"ORD-7-{attempt}", "450.00", -2));
            Assert.Equal(attempt == 1, (await limited.CompleteAsync(PatientId, 7, session.AttemptId)).CanRetry);
        }
        var noMore = await Assert.ThrowsAsync<MedicinePaymentConflictException>(() => limited.StartAsync(PatientId, 7, Billing(), Token, Origin));
        Assert.Contains("choose another payment method", noMore.Message);
    }

    // Security: only a result signed with the merchant secret, from our merchant, is applied; a signed result for another
    // amount is not trusted; results for unknown orders are ignored; another patient cannot read someone else's try.
    [Fact]
    public async Task Notify_ForgedOrTamperedResult_IsNotApplied()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var service = CreateService(dbContext);
        var session = await service.StartAsync(PatientId, 7, Billing(), Token, Origin);

        var forged = Notification("ORD-7-1", "450.00", 2);
        forged["md5sig"] = PayHerePaymentGateway.Md5("forged");
        await Assert.ThrowsAsync<InvalidWebhookSignatureException>(() => service.HandleNotificationAsync(forged));
        await Assert.ThrowsAsync<InvalidWebhookSignatureException>(() => service.HandleNotificationAsync(Notification("ORD-7-1", "450.00", 2, merchantId: "9999999")));
        var unsigned = Notification("ORD-7-1", "450.00", 2);
        unsigned.Remove("md5sig");
        await Assert.ThrowsAsync<InvalidWebhookSignatureException>(() => service.HandleNotificationAsync(unsigned));
        Assert.False(await service.HandleNotificationAsync(Notification("ORD-99-1", "450.00", 2)));
        Assert.Equal(OnlinePaymentAttemptStatus.Pending, (await dbContext.OnlinePaymentAttempts.AsNoTracking().SingleAsync()).Status);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CompleteAsync(Guid.NewGuid(), 7, session.AttemptId));

        await service.HandleNotificationAsync(Notification("ORD-7-1", "1.00", 2));
        var tampered = await service.CompleteAsync(PatientId, 7, session.AttemptId);
        Assert.Equal((OnlinePaymentResultDto.Failed, "verification_failed"), (tampered.Outcome, tampered.FailureCode));
        Assert.NotEqual(MedicinePaymentStatus.Paid, (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Status);
    }

    // Kafka: the consumed events notify the patient (failure with retry advice, then success) and tell staff about the
    // successful payment, once each even when PayHere or Kafka deliver the same result twice.
    [Fact]
    public async Task ConsumedEvents_NotifyPatientAndStaffOnce()
    {
        await using var dbContext = CreateDbContext();
        await ChooseAsync(dbContext, "Online");
        var service = CreateService(dbContext);
        await service.StartAsync(PatientId, 7, Billing(), Token, Origin);
        await service.HandleNotificationAsync(Notification("ORD-7-1", "450.00", -2, message: "Card expired"));
        await service.StartAsync(PatientId, 7, Billing(), Token, Origin);
        await service.HandleNotificationAsync(Notification("ORD-7-2", "450.00", 2));
        await service.HandleNotificationAsync(Notification("ORD-7-2", "450.00", 2));
        Assert.Equal(2, await dbContext.EventOutboxEntries.CountAsync());
        var handler = new KafkaNotificationHandler(dbContext);

        foreach (var entry in await dbContext.EventOutboxEntries.AsNoTracking().OrderBy(item => item.Id).ToListAsync())
        {
            await handler.HandleAsync(entry.Topic, entry.PayloadJson);
            await handler.HandleAsync(entry.Topic, entry.PayloadJson);
        }

        var notifications = await dbContext.PatientNotifications.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(new[] { "Your online payment failed", "Your online payment was successful" }, notifications.Select(item => item.Title));
        Assert.Contains("Card expired Your order is not paid; you can try again.", notifications[0].Message);
        Assert.Equal((OnlinePaymentNotificationStatuses.Failed, OnlinePaymentNotificationStatuses.Succeeded),
            (notifications[0].OrderStatus, notifications[1].OrderStatus));
        Assert.Contains("(sandbox test payment)", notifications[1].Message);
        var staff = await dbContext.PaymentStaffNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(("Online payment received", EventTypes.OnlinePaymentSucceeded, 7), (staff.Title, staff.NotificationType, staff.OrderId));
    }

    // Security: only patients use the payment endpoints; the notify endpoint is open to PayHere (it has no login) but only
    // signed results count; the gateway works only in the sandbox unless live payments are allowed, and needs a notify URL.
    [Fact]
    public async Task Security_PatientOnlyEndpoints_AndSandboxOnlySettings()
    {
        Assert.Equal(AuthorizationPolicies.Patient, typeof(OnlinePaymentsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.NotNull(typeof(PayHereNotifyController).GetCustomAttribute<AllowAnonymousAttribute>());
        var authorization = new ServiceCollection().AddLogging().AddDoctorPaymentAuthorization().BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();
        foreach (var role in new[] { "PharmacyStaff", "Admin", "Doctor" })
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test"));
            Assert.False((await authorization.AuthorizeAsync(user, AuthorizationPolicies.Patient)).Succeeded);
        }

        Assert.True(Settings().IsConfigured);
        Assert.False(Settings(sandbox: false).IsConfigured);
        Assert.True(Settings(sandbox: false, allowLive: true).IsConfigured);
        Assert.False(Settings(notifyUrl: "").IsConfigured);
        Assert.False(Settings(notifyUrl: "not-a-url").IsConfigured);
    }

    private static StartOnlinePaymentRequestDto Billing(string firstName = "Nimal", string email = "nimal@example.com", string phone = "0771234567",
        string city = "Colombo") =>
        new() { FirstName = firstName, LastName = "Perera", Email = email, Phone = phone, Address = "12 Galle Road", City = city };

    /// <summary>A result as PayHere posts it to the notify URL, signed with the merchant secret like PayHere does.</summary>
    private static Dictionary<string, string> Notification(string orderId, string amount, int statusCode, string? message = null, string method = "VISA",
        string merchantId = MerchantId)
    {
        var status = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Dictionary<string, string>
        {
            ["merchant_id"] = merchantId,
            ["order_id"] = orderId,
            ["payment_id"] = "320025071234",
            ["payhere_amount"] = amount,
            ["payhere_currency"] = "LKR",
            ["status_code"] = status,
            ["md5sig"] = PayHerePaymentGateway.Md5(merchantId + orderId + amount + "LKR" + status + PayHerePaymentGateway.Md5(MerchantSecret)),
            ["method"] = method,
            ["status_message"] = message ?? "Successfully completed the payment."
        };
    }

    private static OnlinePaymentEventData EventData(EventOutbox entry) =>
        JsonSerializer.Deserialize<PaymentEvent>(entry.PayloadJson)!.Data.Deserialize<OnlinePaymentEventData>()!;

    /// <summary>Order ORD-{id}: 2 x Iodex and 1 x Panadol.</summary>
    private static PharmacyOrderSnapshot Order(int id, decimal total, string status = "Pending") =>
        new(id, $"ORD-{id}", status, total, new List<PharmacyOrderLine> { new("Iodex", 2, (total - 50m) / 2), new("Panadol", 1, 50m) });

    private static IPharmacyOrderClient Pharmacy(PharmacyOrderSnapshot? order)
    {
        var pharmacy = new Mock<IPharmacyOrderClient>();
        pharmacy.Setup(client => client.GetOrderAsync(It.IsAny<int>(), Token, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        return pharmacy.Object;
    }

    private static PayHereSettings Settings(string merchantSecret = MerchantSecret, bool sandbox = true, bool allowLive = false, int maxAttempts = 5,
        string notifyUrl = NotifyUrl) =>
        new()
        {
            MerchantId = MerchantId, MerchantSecret = merchantSecret, Sandbox = sandbox, AllowLivePayments = allowLive,
            NotifyUrl = notifyUrl, Currency = "LKR", MaxAttempts = maxAttempts
        };

    /// <summary>The patient chooses the method for order 7 (LKR 450), as in GCN-39.</summary>
    private static async Task ChooseAsync(AppDbContext dbContext, string method)
    {
        var payments = new MedicinePaymentService(new MedicinePaymentRepository(dbContext), Pharmacy(Order(7, 450m)),
            Options.Create(new MedicinePaymentSettings { CashMaxAmount = 25_000m }), new FakeTimeProvider(new DateTimeOffset(Now)));
        await payments.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = method }, Token, customerName: "Nimal Perera");
    }

    private static OnlinePaymentService CreateService(AppDbContext dbContext, IPharmacyOrderClient? pharmacy = null, PayHereSettings? settings = null)
    {
        var options = Options.Create(settings ?? Settings());
        return new OnlinePaymentService(new OnlinePaymentRepository(dbContext), pharmacy ?? Pharmacy(Order(7, 450m)),
            new PayHerePaymentGateway(options, NullLogger<PayHerePaymentGateway>.Instance),
            Options.Create(new MedicinePaymentSettings { CashMaxAmount = 25_000m }), options,
            new FakeTimeProvider(new DateTimeOffset(Now)), NullLogger<OnlinePaymentService>.Instance);
    }

    private static AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"OnlinePayments-{Guid.NewGuid():N}").Options);
}
