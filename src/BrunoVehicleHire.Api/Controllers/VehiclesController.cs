using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Application.Vehicles.Dtos;
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
        [FromQuery] bool showInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetVehiclesQuery(page, pageSize, search, showInactive), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Fetches a single vehicle's full record for the detail page (Story 2.5) -- including a
    /// soft-deleted vehicle's, since <see cref="GetVehicleByIdQuery"/> deliberately uses the
    /// unfiltered lookup so the detail page can show its active/soft-deleted state.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(VehicleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var dto = await sender.Send(new GetVehicleByIdQuery(id), cancellationToken);

        return Ok(dto);
    }

    [HttpPost]
    [ProducesResponseType(typeof(VehicleDto), StatusCodes.Status201Created)]
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
    [ProducesResponseType(typeof(VehicleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateVehicleCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command with { VehicleId = id }, cancellationToken);

        return Ok(dto);
    }

    /// <summary>
    /// Soft-deletes ("deactivates") the vehicle -- a distinct action-named route rather than
    /// overloading <c>DELETE</c>, since this app never pairs Vehicle's soft-delete with a true
    /// hard-delete endpoint (see spec-2-3's Design Notes).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SoftDeleteVehicleCommand(id), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Restores a soft-deleted vehicle -- the reverse of <see cref="Deactivate"/>. Unlike
    /// <c>SoftDelete</c>, this is deliberately non-idempotent: restoring an already-active vehicle
    /// throws <see cref="Domain.Exceptions.DomainRuleViolationException"/>, mapped to a 409 by
    /// <see cref="ExceptionHandling.GlobalExceptionHandler"/>.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RestoreVehicleCommand(id), cancellationToken);

        return NoContent();
    }
}
