using BrunoVehicleHire.Application.Vehicles.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.Controllers;

/// <summary>
/// The first real CQRS slice's HTTP entry point: sends <see cref="GetVehiclesQuery"/> via
/// <see cref="ISender"/> and returns its <c>PagedResult&lt;VehicleDto&gt;</c> result directly.
/// Deliberately carries no [Authorize]/[AllowAnonymous] attribute -- protection comes entirely
/// from the global FallbackPolicy registered in Program.cs (Story 1.4). Validation of
/// page/pageSize/search happens entirely in <see cref="GetVehiclesQueryValidator"/> via the shared
/// MediatR pipeline (AD-10) -- never here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VehiclesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetVehiclesQuery(page, pageSize, search), cancellationToken);

        return Ok(result);
    }
}
