using System.Security.Claims;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// Pharmacy staff and admins view medicine payment transactions and order statuses. Only pharmacy staff record payment
/// outcomes and update order statuses; admins are refused (403) and nothing changes.
/// </summary>
[ApiController]
[Route("api/staff/medicine-payments")]
[Authorize(Policy = AuthorizationPolicies.PaymentViewing)]
public class StaffMedicinePaymentsController : ControllerBase
{
    private const string NotFoundMessage = "We could not find that payment transaction.";

    private readonly IMedicinePaymentTransactionService _transactionService;

    public StaffMedicinePaymentsController(IMedicinePaymentTransactionService transactionService) => _transactionService = transactionService;

    /// <summary>
    /// Lists transactions. <paramref name="status"/> is Pending, Successful, Failed, Rejected or All (default);
    /// <paramref name="method"/> is Online, BankTransfer or Cash; <paramref name="search"/> matches order number or customer name.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MedicinePaymentTransactionListDto>> List([FromQuery] string? status, [FromQuery] string? method,
        [FromQuery] string? search, CancellationToken cancellationToken)
    {
        try { return Ok(await _transactionService.ListAsync(status, method, search, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    /// <summary>Pharmacy staff and admins: orders that have been collected (completed), with their payments.</summary>
    [HttpGet("completed")]
    public async Task<ActionResult<List<MedicinePaymentTransactionSummaryDto>>> ListCompleted([FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await _transactionService.ListCompletedAsync(search, cancellationToken));

    [HttpGet("{paymentId:int}")]
    public async Task<ActionResult<MedicinePaymentTransactionDetailDto>> Get(int paymentId, CancellationToken cancellationToken)
    {
        var transaction = await _transactionService.GetAsync(paymentId, IsPharmacyStaff(), AuthorizationHeader(), cancellationToken);
        return transaction is null ? NotFound(new { message = NotFoundMessage }) : Ok(transaction);
    }

    /// <summary>Marks a payment Paid or Failed (a note is required for Failed) when its current status allows it.</summary>
    [HttpPut("{paymentId:int}/status")]
    [Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
    public Task<ActionResult<MedicinePaymentTransactionDetailDto>> UpdateStatus(int paymentId, UpdateMedicinePaymentStatusRequestDto request,
        CancellationToken cancellationToken) =>
        Change(staffId => _transactionService.UpdateStatusAsync(paymentId, staffId, AuthorizationPolicies.PharmacyStaff, request, cancellationToken));

    /// <summary>
    /// Marks the order Ready for Collection, Rejected (reason required) or Collected. The pharmacy-order service applies it;
    /// a Kafka event is then published and the patient notified.
    /// </summary>
    [HttpPut("{paymentId:int}/order-status")]
    [Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
    public Task<ActionResult<MedicinePaymentTransactionDetailDto>> UpdateOrderStatus(int paymentId, UpdateMedicineOrderStatusRequestDto request,
        CancellationToken cancellationToken) =>
        Change(staffId => _transactionService.UpdateOrderStatusAsync(paymentId, staffId, request, AuthorizationHeader(), cancellationToken));

    /// <summary>
    /// The same order status update, addressed by pharmacy order id (the Medicine Orders page). 404 with code
    /// "NoPaymentRecord" when the order has no payment here, so the caller can update the pharmacy directly instead.
    /// </summary>
    [HttpPut("~/api/staff/medicine-orders/{orderId:int}/status")]
    [Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
    public async Task<ActionResult<MedicinePaymentTransactionDetailDto>> UpdateOrderStatusByOrder(int orderId, UpdateMedicineOrderStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var paymentId = await _transactionService.FindPaymentIdForOrderAsync(orderId, cancellationToken);
        if (paymentId is null)
        {
            return NotFound(new { message = "This order has no payment record in payment management.", code = "NoPaymentRecord" });
        }

        return await Change(staffId => _transactionService.UpdateOrderStatusAsync(paymentId.Value, staffId, request, AuthorizationHeader(), cancellationToken));
    }

    private async Task<ActionResult<MedicinePaymentTransactionDetailDto>> Change(Func<Guid, Task<MedicinePaymentTransactionDetailDto?>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffId) || staffId == Guid.Empty)
        {
            return Unauthorized(new { message = "Please sign in again." });
        }

        try
        {
            var transaction = await action(staffId);
            return transaction is null ? NotFound(new { message = NotFoundMessage }) : Ok(transaction);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidPaymentStatusChangeException exception) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (InvalidOrderStatusChangeException exception) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (OrderServiceUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (OrderStatusRecordFailedException exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = exception.Message });
        }
    }

    private bool IsPharmacyStaff() => User.IsInRole(AuthorizationPolicies.PharmacyStaff) || User.IsInRole("Pharmacy Staff");

    private string? AuthorizationHeader() => Request.Headers[HeaderNames.Authorization].FirstOrDefault();
}
