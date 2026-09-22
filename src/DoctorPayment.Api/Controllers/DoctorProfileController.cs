using System.Security.Claims;
using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// Lets an authenticated doctor view and maintain their own professional profile.
/// The profile is always resolved from the JWT subject, so one doctor can never read or edit the profile of another.
/// </summary>
[ApiController]
[Route("api/doctors/me/profile")]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
public class DoctorProfileController : ControllerBase
{
    private readonly IDoctorProfileService _profileService;
    private readonly IValidator<UpdateDoctorProfileRequestDto> _validator;

    public DoctorProfileController(IDoctorProfileService profileService, IValidator<UpdateDoctorProfileRequestDto> validator)
    {
        _profileService = profileService;
        _validator = validator;
    }

    [HttpGet]
    public async Task<ActionResult<DoctorProfileResponseDto>> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        var profile = await _profileService.GetByDoctorIdAsync(doctorId, cancellationToken);
        return profile is null
            ? NotFound(new { message = "You have not created a professional profile yet." })
            : Ok(profile);
    }

    [HttpPut]
    public async Task<ActionResult<DoctorProfileResponseDto>> SaveMine(UpdateDoctorProfileRequestDto request, CancellationToken cancellationToken)
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

        try { return Ok(await _profileService.SaveAsync(doctorId, request, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteMine(CancellationToken cancellationToken)
    {
        if (!TryGetDoctorId(out var doctorId)) return Unauthorized(new { message = "The token does not identify a doctor." });

        return await _profileService.DeleteAsync(doctorId, cancellationToken)
            ? NoContent()
            : NotFound(new { message = "You have not created a professional profile yet." });
    }

    private bool TryGetDoctorId(out Guid doctorId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out doctorId) && doctorId != Guid.Empty;
}
