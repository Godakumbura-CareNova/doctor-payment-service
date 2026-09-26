using System.Security.Claims;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// Pharmacy staff verify bank transfer receipts. Only a verification marks a medicine payment Paid; a rejection lets
/// the patient upload another receipt.
/// </summary>
[ApiController]
[Route("api/staff/bank-transfer-receipts")]
[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
public class StaffBankTransferReceiptsController : ControllerBase
{
    private readonly IBankTransferReceiptService _receiptService;

    public StaffBankTransferReceiptsController(IBankTransferReceiptService receiptService) => _receiptService = receiptService;

    /// <summary>Lists receipts. <paramref name="status"/> is PendingVerification, Verified, Rejected or All (the default is PendingVerification).</summary>
    [HttpGet]
    public async Task<ActionResult<List<StaffBankTransferReceiptDto>>> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        BankTransferReceiptStatus? filter = BankTransferReceiptStatus.PendingVerification;
        if (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) filter = null;
        else if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BankTransferReceiptStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest(new { message = "Status must be PendingVerification, Verified, Rejected or All." });
            }
            filter = parsed;
        }

        return Ok(await _receiptService.ListAsync(filter, cancellationToken));
    }

    [HttpGet("{receiptId:int}/file")]
    public async Task<IActionResult> GetFile(int receiptId, CancellationToken cancellationToken)
    {
        var file = await _receiptService.OpenFileAsync(receiptId, cancellationToken);
        return file is null
            ? NotFound(new { message = "That receipt file could not be found." })
            : File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("{receiptId:int}/verify")]
    public Task<ActionResult<StaffBankTransferReceiptDto>> Verify(int receiptId, CancellationToken cancellationToken) =>
        Review(staffId => _receiptService.VerifyAsync(receiptId, staffId, cancellationToken));

    [HttpPost("{receiptId:int}/reject")]
    public Task<ActionResult<StaffBankTransferReceiptDto>> Reject(int receiptId, RejectBankTransferReceiptDto request, CancellationToken cancellationToken) =>
        Review(staffId => _receiptService.RejectAsync(receiptId, staffId, request.Reason, cancellationToken));

    [HttpGet("~/api/staff/payment-notifications")]
    public async Task<ActionResult<List<PaymentStaffNotificationDto>>> Notifications(CancellationToken cancellationToken) =>
        Ok(await _receiptService.ListNotificationsAsync(cancellationToken));

    [HttpPut("~/api/staff/payment-notifications/{notificationId:int}/read")]
    public async Task<IActionResult> MarkNotificationRead(int notificationId, CancellationToken cancellationToken) =>
        await _receiptService.MarkNotificationReadAsync(notificationId, cancellationToken)
            ? NoContent()
            : NotFound(new { message = "That notification no longer exists." });

    private async Task<ActionResult<StaffBankTransferReceiptDto>> Review(Func<Guid, Task<StaffBankTransferReceiptDto?>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffId) || staffId == Guid.Empty)
        {
            return Unauthorized(new { message = "Please sign in again." });
        }

        try
        {
            var receipt = await action(staffId);
            return receipt is null ? NotFound(new { message = "That receipt no longer exists." }) : Ok(receipt);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (ReceiptReviewConflictException exception) { return Conflict(new { message = exception.Message }); }
    }
}
