using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Application.Vehicles.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.Controllers;

/// <summary>
/// The Vehicle aggregate's HTTP entry point: sends <see cref="GetVehiclesQuery"/>/
/// <see cref="CreateVehicleCommand"/>/<see cref="UpdateVehicleCommand"/> via <see cref="ISender"/>
/// and returns each result directly.
/// Deliberately carries no [Authorize]/[AllowAnonymous] attribute -- protection comes entirely
/// from the global FallbackPolicy registered in Program.cs (Story 1.4). Validation of every
/// request shape happens entirely in each request's own validator via the shared MediatR pipeline
/// (AD-10) -- never here.
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

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateVehicleCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command, cancellationToken);

        return Created($"/api/vehicles/{dto.Id}", dto);
    }

    /// <summary>
    /// The route id always wins over any <c>vehicleId</c> present in the request body (the resource
    /// identity comes from the URL, the body describes the desired state) -- avoids a second
    /// near-duplicate request DTO purely to split route-bound from body-bound fields.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateVehicleCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command with { VehicleId = id }, cancellationToken);

        return Ok(dto);
    }
}
