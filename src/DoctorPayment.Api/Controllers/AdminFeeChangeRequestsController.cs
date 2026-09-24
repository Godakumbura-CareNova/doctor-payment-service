using System.Security.Claims;
using DoctorPayment.Api.DTOs.FeeChangeRequest;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>Admins review doctors' fee-change requests. Only an approval changes a doctor's consultation fee.</summary>
[ApiController]
[Route("api/admin/fee-change-requests")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public class AdminFeeChangeRequestsController : ControllerBase
{
    private readonly IFeeChangeRequestService _feeChangeService;
    private readonly IValidator<RejectFeeChangeRequestDto> _rejectValidator;

    public AdminFeeChangeRequestsController(IFeeChangeRequestService feeChangeService, IValidator<RejectFeeChangeRequestDto> rejectValidator)
    {
        _feeChangeService = feeChangeService;
        _rejectValidator = rejectValidator;
    }

    /// <summary>Lists requests. <paramref name="status"/> is Pending, Approved, Rejected or All (the default is Pending).</summary>
    [HttpGet]
    public async Task<ActionResult<List<AdminFeeChangeRequestDto>>> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        FeeChangeRequestStatus? filter = FeeChangeRequestStatus.Pending;
        if (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) filter = null;
        else if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<FeeChangeRequestStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest(new { message = "Status must be Pending, Approved, Rejected or All." });
            }
            filter = parsed;
        }

        return Ok(await _feeChangeService.ListAsync(filter, cancellationToken));
    }

    [HttpPost("{requestId:int}/approve")]
    public async Task<ActionResult<AdminFeeChangeRequestDto>> Approve(int requestId, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { message = "The token does not identify an admin." });

        try
        {
            var approved = await _feeChangeService.ApproveAsync(requestId, adminId, cancellationToken);
            return approved is null ? NotFound(new { message = "That fee-change request no longer exists." }) : Ok(approved);
        }
        catch (FeeChangeRequestConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPost("{requestId:int}/reject")]
    public async Task<ActionResult<AdminFeeChangeRequestDto>> Reject(int requestId, RejectFeeChangeRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { message = "The token does not identify an admin." });

        var validation = await _rejectValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                message = "Please correct the highlighted fields.",
                errors = validation.ToDictionary()
            });
        }

        try
        {
            var rejected = await _feeChangeService.RejectAsync(requestId, adminId, request.Reason!, cancellationToken);
            return rejected is null ? NotFound(new { message = "That fee-change request no longer exists." }) : Ok(rejected);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (FeeChangeRequestConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    private bool TryGetAdminId(out Guid adminId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId) && adminId != Guid.Empty;
}
