using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PixousDocIntelligence.Controllers;

/// <summary>
/// Service health monitoring and heartbeat endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Checks the operational status of the Pixous Document Intelligence API service.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "Pixous Document Intelligence API",
            framework = ".NET 8",
            vlm = "Gemini 2.5 Flash",
            timestamp = DateTime.UtcNow
        });
    }
}
