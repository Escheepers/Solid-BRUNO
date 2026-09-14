using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Bookings.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.Controllers;

/// <summary>
/// The Booking aggregate's HTTP entry point: sends <see cref="GetBookingsQuery"/>/
/// <see cref="CreateBookingCommand"/> via <see cref="ISender"/> and returns each result directly.
/// Mirrors <see cref="VehiclesController"/>'s shape. Deliberately carries only List/Create for this
/// story -- no Edit/Cancel/View/Deactivate routes yet (spec-4-1's Never section: those belong to
/// later Epic 4 stories). Deliberately carries no [Authorize]/[AllowAnonymous] attribute --
/// protection comes entirely from the global FallbackPolicy registered in Program.cs (Story 1.4).
/// Validation of every request shape happens entirely in each request's own validator via the shared
/// MediatR pipeline (AD-10) -- never here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BookingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetBookingsQuery(page, pageSize), cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command, cancellationToken);

        return Created($"/api/bookings/{dto.Id}", dto);
    }
}
