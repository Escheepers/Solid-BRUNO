using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Application.Customers.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.Controllers;

/// <summary>
/// The Customer aggregate's HTTP entry point: sends <see cref="GetCustomersQuery"/>/
/// <see cref="CreateCustomerCommand"/> via <see cref="ISender"/> and returns each result directly.
/// Deliberately carries no [Authorize]/[AllowAnonymous] attribute -- protection comes entirely from
/// the global FallbackPolicy registered in Program.cs (Story 1.4). Validation of every request shape
/// happens entirely in each request's own validator via the shared MediatR pipeline (AD-10) -- never
/// here. Mirrors <c>VehiclesController</c>'s exact shape.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CustomersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetCustomersQuery(page, pageSize, search), cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command, cancellationToken);

        return Created($"/api/customers/{dto.Id}", dto);
    }
}
