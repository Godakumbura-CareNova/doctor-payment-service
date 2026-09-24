using DoctorPayment.Api.Clients;
using DoctorPayment.Api.Config;
using DoctorPayment.Api.Data;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services;
using DoctorPayment.Api.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DoctorPayment.Api.Tests.Unit;

/// <summary>
/// Choosing a payment method for a medicine order. Runs the real service and repository against the in-memory
/// provider; the pharmacy-order service is a mock.
/// </summary>
public sealed class MedicinePaymentTests
{
    private const string Token = "Bearer patient-token";
    private static readonly DateTime Now = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.NewGuid();

    // Scenario 1: online payment moves the order on to the online payment step.
    [Fact]
    public async Task SelectAsync_Online_AwaitsOnlinePayment()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 450m));

        var payment = await service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "online" }, Token);

        Assert.Equal(("Online", "AwaitingOnlinePayment", "OnlinePayment"), (payment.Method, payment.Status, payment.NextStep));
        Assert.Equal((450m, "ORD-7"), (payment.Amount, payment.OrderNumber));
        Assert.Null(payment.BankTransfer);
    }

    // Scenario 2: bank transfer returns the account to pay into, quoting the order number.
    [Fact]
    public async Task SelectAsync_BankTransfer_ReturnsTheInstructions()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 450m));

        var payment = await service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "BankTransfer" }, Token);

        Assert.Equal(("AwaitingBankTransfer", "BankTransferInstructions"), (payment.Status, payment.NextStep));
        Assert.Equal("Test Bank", payment.BankTransfer!.BankName);
        Assert.Equal("123-456", payment.BankTransfer.AccountNumber);
        Assert.Equal(("ORD-7", 450m), (payment.BankTransfer.Reference, payment.BankTransfer.Amount));
    }

    // Scenario 3: cash is recorded as pay-on-collection and no online payment starts.
    [Fact]
    public async Task SelectAsync_Cash_IsPaidOnCollection()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 450m));

        var payment = await service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "Cash" }, Token);

        Assert.Equal(("Cash", "PayOnCollection", "PayOnCollection"), (payment.Method, payment.Status, payment.NextStep));
    }

    // Choosing again changes the method on the same record instead of adding a second payment.
    [Fact]
    public async Task SelectAsync_Again_ChangesTheMethodOfTheSamePayment()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 450m));

        var first = await service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "Online" }, Token);
        var second = await service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "Cash" }, Token);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Cash", (await dbContext.MedicinePayments.AsNoTracking().SingleAsync()).Method.ToString());
        Assert.Equal("Cash", (await service.GetSelectedAsync(PatientId, 7))!.Method);
    }

    // Scenario 4: no method (or an unknown one) is refused with a prompt to choose.
    [Fact]
    public async Task Validator_RequiresAKnownMethod()
    {
        var validator = new SelectPaymentMethodValidator();

        Assert.Equal("Select a payment method.", validator.Validate(new SelectPaymentMethodRequestDto()).Errors.Single().ErrorMessage);
        Assert.Equal("Select a payment method.", validator.Validate(new SelectPaymentMethodRequestDto { Method = "  " }).Errors.Single().ErrorMessage);
        Assert.Equal("Choose online payment, bank transfer or cash.", validator.Validate(new SelectPaymentMethodRequestDto { Method = "Cheque" }).Errors.Single().ErrorMessage);
        Assert.Equal("Choose online payment, bank transfer or cash.", validator.Validate(new SelectPaymentMethodRequestDto { Method = "1" }).Errors.Single().ErrorMessage);
        Assert.True(validator.Validate(new SelectPaymentMethodRequestDto { Method = "bankTransfer" }).IsValid);

        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 450m));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto(), Token));
        Assert.Empty(dbContext.MedicinePayments);
    }

    // The options list every method and mark which can be chosen.
    [Fact]
    public async Task GetOptionsAsync_ListsEveryMethodWithTheOrderTotal()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 450m));

        var options = await service.GetOptionsAsync(PatientId, 7, Token);

        Assert.Equal(("ORD-7", 450m), (options.OrderNumber, options.Amount));
        Assert.Equal(new[] { "Online", "BankTransfer", "Cash" }, options.Methods.Select(method => method.Method));
        Assert.All(options.Methods, method => Assert.True(method.Available));
        Assert.Null(options.SelectedPayment);
    }

    // Cash is not offered above the configured limit, at checkout or for the order, and choosing it anyway is refused.
    [Fact]
    public async Task Cash_IsUnavailable_AboveTheCashLimit()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, Order(7, 30_000m));

        var cash = (await service.GetOptionsAsync(PatientId, 7, Token)).Methods.Single(method => method.Method == "Cash");
        Assert.False(cash.Available);
        Assert.Equal("Cash is only accepted for orders up to LKR 25,000.00. Please choose online payment or bank transfer.", cash.UnavailableReason);

        var exception = await Assert.ThrowsAsync<PaymentMethodUnavailableException>(() =>
            service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "Cash" }, Token));
        Assert.Equal(cash.UnavailableReason, exception.Message);
        Assert.Empty(dbContext.MedicinePayments);

        Assert.False(service.GetMethods(30_000m).Single(method => method.Method == "Cash").Available);
        Assert.True(service.GetMethods(450m).Single(method => method.Method == "Cash").Available);
    }

    // A method switched off in configuration, or bank transfer without an account, is shown as unavailable.
    [Fact]
    public async Task DisabledOrUnconfiguredMethods_AreUnavailable()
    {
        await using var dbContext = CreateDbContext();
        var settings = Settings();
        settings.EnabledMethods = new[] { "BankTransfer", "Cash" };
        settings.BankTransfer = new BankTransferSettings();
        var service = CreateService(dbContext, Order(7, 450m), settings);

        var methods = (await service.GetOptionsAsync(PatientId, 7, Token)).Methods.ToDictionary(method => method.Method);

        Assert.Equal("Online payment is not available right now. Please choose another payment method.", methods["Online"].UnavailableReason);
        Assert.Equal("Bank transfer is not available right now. Please choose another payment method.", methods["BankTransfer"].UnavailableReason);
        Assert.True(methods["Cash"].Available);
        await Assert.ThrowsAsync<PaymentMethodUnavailableException>(() =>
            service.SelectAsync(PatientId, 7, new SelectPaymentMethodRequestDto { Method = "Online" }, Token));
    }

    // Scenario 5: when the pharmacy-order service is down the failure is reported so the patient can retry.
    [Fact]
    public async Task OrderServiceFailure_IsReported()
    {
        await using var dbContext = CreateDbContext();
        var orders = new Mock<IPharmacyOrderClient>();
        orders.Setup(client => client.GetOrderAsync(7, Token, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderServiceUnavailableException("We could not load your order right now. Please try again."));
        var service = CreateService(dbContext, orders.Object);

        await Assert.ThrowsAsync<OrderServiceUnavailableException>(() => service.GetOptionsAsync(PatientId, 7, Token));
    }

    // Scenario 6: an order that is missing, rejected, collected or inconsistent cannot start a payment.
    [Fact]
    public async Task InvalidOrders_CannotStartAPayment()
    {
        await using var dbContext = CreateDbContext();
        var cash = new SelectPaymentMethodRequestDto { Method = "Cash" };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(dbContext, (PharmacyOrderSnapshot?)null).SelectAsync(PatientId, 7, cash, Token));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(dbContext, Order(7, 450m)).SelectAsync(PatientId, 0, cash, Token));

        var cases = new (PharmacyOrderSnapshot Order, string Message)[]
        {
            (Order(7, 450m) with { Status = "Rejected" }, "This order was rejected by the pharmacy, so it cannot be paid for."),
            (Order(7, 450m) with { Status = "Collected" }, "This order has already been collected."),
            (Order(7, 450m) with { Items = new List<PharmacyOrderLine>() }, "This order's details are incomplete, so payment cannot start. Please contact the pharmacy."),
            (Order(7, 450m) with { TotalAmount = 999m }, "This order's details are incomplete, so payment cannot start. Please contact the pharmacy."),
            (Order(7, 450m) with { OrderNumber = "" }, "This order's details are incomplete, so payment cannot start. Please contact the pharmacy."),
            (Order(8, 450m), "This order's details are incomplete, so payment cannot start. Please contact the pharmacy.")
        };
        foreach (var (order, message) in cases)
        {
            var exception = await Assert.ThrowsAsync<InvalidMedicineOrderException>(() => CreateService(dbContext, order).SelectAsync(PatientId, 7, cash, Token));
            Assert.Equal(message, exception.Message);
        }

        Assert.Empty(dbContext.MedicinePayments);
    }

    private static PharmacyOrderSnapshot Order(int id, decimal total) =>
        new(id, $"ORD-{id}", "Pending", total, new List<PharmacyOrderLine> { new("Iodex", 1, total) });

    private static MedicinePaymentSettings Settings() => new()
    {
        CashMaxAmount = 25_000m,
        BankTransfer = new BankTransferSettings { BankName = "Test Bank", Branch = "Colombo", AccountName = "CareNova Pharmacy", AccountNumber = "123-456" }
    };

    private static MedicinePaymentService CreateService(AppDbContext dbContext, PharmacyOrderSnapshot? order, MedicinePaymentSettings? settings = null)
    {
        var orders = new Mock<IPharmacyOrderClient>();
        orders.Setup(client => client.GetOrderAsync(It.IsAny<int>(), Token, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        return CreateService(dbContext, orders.Object, settings);
    }

    private static MedicinePaymentService CreateService(AppDbContext dbContext, IPharmacyOrderClient orders, MedicinePaymentSettings? settings = null) =>
        new(new MedicinePaymentRepository(dbContext), orders, Options.Create(settings ?? Settings()), new FakeTimeProvider(new DateTimeOffset(Now)));

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MedicinePayments-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }
}
