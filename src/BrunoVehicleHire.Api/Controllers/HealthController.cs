using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.Controllers;

/// <summary>
/// Minimal health endpoint, intentionally trivial (no dependency on AppDbContext or any domain
/// type) -- this story stays scoped to proving the auth mechanism, not building a real feature.
///
/// Deliberately carries no [Authorize]/[AllowAnonymous] attribute: protection comes entirely
/// from the global FallbackPolicy registered in Program.cs, proving the fallback covers *any*
/// future controller automatically.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
