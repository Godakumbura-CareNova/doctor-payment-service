using Microsoft.AspNetCore.Mvc;

namespace DoctorPayment.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Get()
    {
        return Ok(new { status = "Healthy" });
    }
}
