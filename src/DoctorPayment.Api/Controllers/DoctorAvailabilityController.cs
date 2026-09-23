using System.Security.Claims;
using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// Lets a doctor publish, edit and withdraw the consultation slots patients see on their public profile.
/// Like the profile, slots are always resolved from the JWT subject.
/// </summary>
[ApiController]
[Route("api/doctors/me/availability")]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
public class DoctorAvailabilityController : ControllerBase
{
    private readonly IDoctorAvailabilityService _availabilityService;
    private readonly IValidator<CreateAvailabilityRequestDto> _validator;
    private readonly IValidator<UpdateAvailabilitySlotRequestDto> _updateValidator;

    public DoctorAvailabilityController(
        IDoctorAvailabilityService availabilityService,
        IValidator<CreateAvailabilityRequestDto> validator,
        IValidator<UpdateAvailabilitySlotRequestDto> updateValidator)
    {
        _availabilityService = availabilityService;
        _validator = validator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    public async Task<ActionResult<List<AvailabilitySlotResponseDto>>> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        return Ok(await _availabilityService.GetMineAsync(doctorId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<List<AvailabilitySlotResponseDto>>> Add(CreateAvailabilityRequestDto request, CancellationToken cancellationToken)
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
            var created = await _availabilityService.AddAsync(doctorId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (DoctorProfileRequiredException exception) { return Conflict(new { message = exception.Message }); }
        catch (AvailabilityConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPut("{slotId:int}")]
    public async Task<ActionResult<AvailabilitySlotResponseDto>> Update(int slotId, UpdateAvailabilitySlotRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
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
            var updated = await _availabilityService.UpdateAsync(doctorId, slotId, request, cancellationToken);
            return updated is null ? NotFound(new { message = "That slot no longer exists." }) : Ok(updated);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (AvailabilityConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpDelete("{slotId:int}")]
    public async Task<IActionResult> Delete(int slotId, CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        try
        {
            return await _availabilityService.DeleteAsync(doctorId, slotId, cancellationToken)
                ? NoContent()
                : NotFound(new { message = "That slot no longer exists." });
        }
        catch (AvailabilityConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    private bool TryGetDoctorId(out Guid doctorId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out doctorId) && doctorId != Guid.Empty;
}
