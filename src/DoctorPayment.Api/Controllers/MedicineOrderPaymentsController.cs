using System.Security.Claims;
using DoctorPayment.Api.DTOs.MedicinePayment;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// A patient choosing how to pay for one of their pharmacy orders. The patient is always the JWT subject, and their
/// token is forwarded to the pharmacy-order service so it decides whether the order is theirs.
/// </summary>
[ApiController]
[Route("api/medicine-orders/{orderId:int}")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class MedicineOrderPaymentsController : ControllerBase
{
    private readonly IMedicinePaymentService _paymentService;
    private readonly IBankTransferReceiptService _receiptService;
    private readonly IValidator<SelectPaymentMethodRequestDto> _validator;

    public MedicineOrderPaymentsController(
        IMedicinePaymentService paymentService,
        IBankTransferReceiptService receiptService,
        IValidator<SelectPaymentMethodRequestDto> validator)
    {
        _paymentService = paymentService;
        _receiptService = receiptService;
        _validator = validator;
    }

    /// <summary>The methods for a checkout total, before the order exists. The choice is re-checked when it is saved.</summary>
    [HttpGet("~/api/medicine-payments/methods")]
    public ActionResult<List<PaymentMethodOptionDto>> GetMethods([FromQuery] decimal? amount)
    {
        try { return Ok(_paymentService.GetMethods(amount)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpGet("payment-options")]
    public Task<ActionResult<MedicinePaymentOptionsDto>> GetOptions(int orderId, CancellationToken cancellationToken) =>
        Execute(patientId => _paymentService.GetOptionsAsync(patientId, orderId, AuthorizationHeader(), cancellationToken));

    [HttpGet("payment")]
    public async Task<ActionResult<MedicinePaymentResponseDto>> GetSelected(int orderId, CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Please sign in again." });

        var payment = await _paymentService.GetSelectedAsync(patientId, orderId, cancellationToken);
        return payment is null ? NotFound(new { message = "No payment method has been chosen for this order yet." }) : Ok(payment);
    }

    /// <summary>Chooses or changes the payment method. Repeating the same choice is safe.</summary>
    [HttpPut("payment")]
    public async Task<ActionResult<MedicinePaymentResponseDto>> Select(int orderId, SelectPaymentMethodRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                message = validation.Errors[0].ErrorMessage,
                errors = validation.ToDictionary()
            });
        }

        return await Execute(patientId => _paymentService.SelectAsync(patientId, orderId, request, AuthorizationHeader(), cancellationToken));
    }

    /// <summary>
    /// Uploads the bank transfer receipt (JPG, JPEG or PNG up to 5 MB, form field "file"). The order stays Pending; the
    /// receipt is sent to the pharmacy for verification. The request limit is above 5 MB so an oversized file gets our message.
    /// </summary>
    [HttpPost("payment/receipt")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
    public Task<ActionResult<BankTransferReceiptUploadResultDto>> UploadReceipt(int orderId, IFormFile? file, CancellationToken cancellationToken) =>
        Execute(patientId => _receiptService.UploadAsync(patientId, User.FindFirstValue(ClaimTypes.Name), orderId, file, AuthorizationHeader(), cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Guid, Task<T>> action)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Please sign in again." });

        try
        {
            return Ok(await action(patientId));
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (InvalidMedicineOrderException exception) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (ReceiptAssociationException exception) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (ReceiptReviewConflictException exception) { return Conflict(new { message = exception.Message }); }
        catch (ReceiptUploadFailedException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (PaymentMethodUnavailableException exception) { return Conflict(new { message = exception.Message }); }
        catch (MedicinePaymentConflictException exception) { return Conflict(new { message = exception.Message }); }
        catch (OrderServiceUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
    }

    private string? AuthorizationHeader() => Request.Headers[HeaderNames.Authorization].FirstOrDefault();

    private bool TryGetPatientId(out Guid patientId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out patientId) && patientId != Guid.Empty;
}
