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
/// GCN-41: a patient paying their medicine order online through PayHere. The patient is the JWT subject, and their token
/// is forwarded to the pharmacy-order service so it confirms the order is theirs. Card details never come here: the
/// patient types them into PayHere's own payment window.
/// </summary>
[ApiController]
[Route("api/medicine-orders/{orderId:int}/payment/online")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class OnlinePaymentsController : ControllerBase
{
    private readonly IOnlinePaymentService _onlinePayments;

    public OnlinePaymentsController(IOnlinePaymentService onlinePayments) => _onlinePayments = onlinePayments;

    [HttpGet]
    public Task<ActionResult<OnlinePaymentStatusDto>> GetStatus(int orderId, CancellationToken cancellationToken) =>
        Execute(patientId => _onlinePayments.GetStatusAsync(patientId, orderId, cancellationToken));

    /// <summary>Starts a payment try and returns the signed PayHere checkout the browser opens.</summary>
    [HttpPost("start")]
    public Task<ActionResult<OnlinePaymentSessionDto>> Start(int orderId, StartOnlinePaymentRequestDto request, CancellationToken cancellationToken) =>
        Execute(patientId => _onlinePayments.StartAsync(patientId, orderId, request, Request.Headers[HeaderNames.Authorization].FirstOrDefault(),
            Request.Headers[HeaderNames.Origin].FirstOrDefault(), cancellationToken));

    /// <summary>The result recorded from PayHere's own notification; the browser's view of the result is never trusted.</summary>
    [HttpPost("{attemptId:int}/complete")]
    public Task<ActionResult<OnlinePaymentResultDto>> Complete(int orderId, int attemptId, CancellationToken cancellationToken) =>
        Execute(patientId => _onlinePayments.CompleteAsync(patientId, orderId, attemptId, cancellationToken));

    /// <summary>The patient closed PayHere's window without paying.</summary>
    [HttpPost("{attemptId:int}/cancel")]
    public Task<ActionResult<OnlinePaymentResultDto>> Cancel(int orderId, int attemptId, CancellationToken cancellationToken) =>
        Execute(patientId => _onlinePayments.CancelAsync(patientId, orderId, attemptId, cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Guid, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var patientId) || patientId == Guid.Empty)
        {
            return Unauthorized(new { message = "Please sign in again." });
        }

        try
        {
            return Ok(await action(patientId));
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (InvalidMedicineOrderException exception) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (PaymentMethodUnavailableException exception) { return Conflict(new { message = exception.Message }); }
        catch (MedicinePaymentConflictException exception) { return Conflict(new { message = exception.Message }); }
        catch (PaymentGatewayUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message, retryable = true });
        }
        catch (OrderServiceUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message, retryable = true });
        }
    }
}

/// <summary>
/// GCN-41: PayHere posts each payment result here (server to server, form encoded). Open to PayHere without a login, but a
/// result is applied only when its MD5 signature, made with the merchant secret, is valid and its amount matches.
/// </summary>
[ApiController]
[Route("api/payments/payhere/notify")]
[AllowAnonymous]
public class PayHereNotifyController : ControllerBase
{
    private readonly IOnlinePaymentService _onlinePayments;
    private readonly ILogger<PayHereNotifyController> _logger;

    public PayHereNotifyController(IOnlinePaymentService onlinePayments, ILogger<PayHereNotifyController> logger)
    {
        _onlinePayments = onlinePayments;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var fields = form.ToDictionary(field => field.Key, field => field.Value.ToString(), StringComparer.Ordinal);
        try
        {
            await _onlinePayments.HandleNotificationAsync(fields, cancellationToken);
            return Ok();
        }
        catch (InvalidWebhookSignatureException exception)
        {
            _logger.LogWarning("PayHere result refused: {Reason}", exception.Message);
            return BadRequest();
        }
    }
}
