using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Security;
using DoctorPayment.Api.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

/// <summary>
/// Patient-facing doctor directory: search doctors by name or specialization and view a doctor's published profile.
/// Read-only; doctors maintain their own profile through <see cref="DoctorProfileController"/>.
/// </summary>
[ApiController]
[Route("api/doctors")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorSearchService _searchService;
    private readonly IValidator<DoctorSearchQueryDto> _validator;

    public DoctorsController(IDoctorSearchService searchService, IValidator<DoctorSearchQueryDto> validator)
    {
        _searchService = searchService;
        _validator = validator;
    }

    [HttpGet]
    public async Task<ActionResult<DoctorSearchResultDto>> Search([FromQuery] DoctorSearchQueryDto query, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                message = "Please check your search criteria.",
                errors = validation.ToDictionary()
            });
        }

        return Ok(await _searchService.SearchAsync(query, cancellationToken));
    }

    [HttpGet("specializations")]
    public async Task<ActionResult<List<string>>> Specializations(CancellationToken cancellationToken) =>
        Ok(await _searchService.GetSpecializationsAsync(cancellationToken));

    [HttpGet("{doctorId:guid}")]
    public async Task<ActionResult<DoctorPublicProfileDto>> GetProfile(Guid doctorId, CancellationToken cancellationToken)
    {
        var profile = await _searchService.GetPublicProfileAsync(doctorId, cancellationToken);
        return profile is null
            ? NotFound(new { message = "This doctor has not published a professional profile." })
            : Ok(profile);
    }
}
