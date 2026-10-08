using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Services.Interfaces;
namespace DoctorPayment.Api.Controllers;
[ApiController, AllowAnonymous, Route("api/public/doctors")]
public class PublicDoctorsController(IDoctorSearchService doctors) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DoctorSearchQueryDto query, CancellationToken token)
    {
        if (query.Page < 1 || query.Page > 100000 || query.PageSize < 1 || query.PageSize > 50 || query.Search?.Length > 200 || query.Specialization?.Length > 200) return BadRequest(new { message = "Please check your search criteria." });
        var result = await doctors.SearchAsync(query, token);
        return Ok(new { items = result.Items.Select(d => new { d.DoctorId, d.FullName, d.Specialization, d.Biography, d.ConsultationFee, d.YearsOfExperience }), result.Page, result.PageSize, result.TotalCount, result.TotalPages });
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken token)
    {
        var d = await doctors.GetPublicProfileAsync(id, token);
        return d is null ? NotFound(new { message = "This doctor profile is unavailable." }) : Ok(new { d.DoctorId, d.FullName, d.Specialization, d.Biography, d.ConsultationFee, d.YearsOfExperience });
    }
}
