using Microsoft.AspNetCore.Mvc;

namespace e600ShopApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    /// <summary>Lightweight liveness probe for load balancers and uptime checks.</summary>
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });
    }
}
