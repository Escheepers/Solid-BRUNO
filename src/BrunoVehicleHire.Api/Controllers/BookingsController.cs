using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Bookings.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.Controllers;

/// <summary>
/// The Booking aggregate's HTTP entry point: sends <see cref="GetBookingsQuery"/>/
/// <see cref="GetBookingByIdQuery"/>/<see cref="CreateBookingCommand"/>/<see cref="CancelBookingCommand"/>
/// via <see cref="ISender"/> and returns each result directly. Mirrors <see cref="VehiclesController"/>'s
/// shape. Deliberately carries no Edit route (still out of this project's scope -- see spec-4-5's
/// Never section). Deliberately carries no [Authorize]/[AllowAnonymous] attribute -- protection comes
/// entirely from the global FallbackPolicy registered in Program.cs (Story 1.4). Validation of every
/// request shape happens entirely in each request's own validator via the shared MediatR pipeline
/// (AD-10) -- never here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BookingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? vehicleId = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetBookingsQuery(page, pageSize, vehicleId, search), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// The Booking detail view's sole endpoint (spec-4-5), mirroring
    /// <see cref="VehiclesController.GetById"/>'s exact shape. A stale/invalid id surfaces as a 404
    /// via <see cref="ExceptionHandling.GlobalExceptionHandler"/>'s <c>NotFoundException</c> branch --
    /// never checked here.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var dto = await sender.Send(new GetBookingByIdQuery(id), cancellationToken);

        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command, cancellationToken);

        return Created($"/api/bookings/{dto.Id}", dto);
    }

    /// <summary>
    /// Cancels the booking (spec-4-3) -- a distinct action-named route, mirroring
    /// <see cref="VehiclesController.Deactivate"/>'s exact shape. Every ineligibility rule
    /// (already-Cancelled, already-Completed, past-EndDate-still-Active) is enforced entirely by
    /// <see cref="Domain.Booking.Cancel"/> and surfaces as a 409 via
    /// <see cref="ExceptionHandling.GlobalExceptionHandler"/> -- never checked here.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelBookingCommand(id), cancellationToken);

        return NoContent();
    }
}
