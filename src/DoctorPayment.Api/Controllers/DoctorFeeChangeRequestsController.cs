using System.Security.Claims;
using DoctorPayment.Api.DTOs.FeeChangeRequest;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// A doctor asking for a new consultation fee and following the request. The fee itself only changes when
/// an admin approves (see <see cref="AdminFeeChangeRequestsController"/>). The doctor is always the JWT subject.
/// </summary>
[ApiController]
[Route("api/doctors/me/fee-change-requests")]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
public class DoctorFeeChangeRequestsController : ControllerBase
{
    private readonly IFeeChangeRequestService _feeChangeService;
    private readonly IValidator<CreateFeeChangeRequestDto> _validator;

    public DoctorFeeChangeRequestsController(IFeeChangeRequestService feeChangeService, IValidator<CreateFeeChangeRequestDto> validator)
    {
        _feeChangeService = feeChangeService;
        _validator = validator;
    }

    [HttpGet]
    public async Task<ActionResult<MyFeeChangeRequestsDto>> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        return Ok(await _feeChangeService.GetMineAsync(doctorId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<FeeChangeRequestResponseDto>> Submit(CreateFeeChangeRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        var validation = await _validator.ValidateAsync(request, cancellationToken);
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
            var created = await _feeChangeService.SubmitAsync(doctorId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message, errors = new Dictionary<string, string[]> { ["RequestedFee"] = new[] { exception.Message } } });
        }
        catch (DoctorProfileRequiredException exception) { return Conflict(new { message = exception.Message }); }
        catch (FeeChangeRequestConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    private bool TryGetDoctorId(out Guid doctorId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out doctorId) && doctorId != Guid.Empty;
}
