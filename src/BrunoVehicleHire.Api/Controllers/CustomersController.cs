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

    /// <summary>
    /// The route id always wins over any <c>customerId</c> present in the request body (the resource
    /// identity comes from the URL, the body describes the desired state) -- avoids a second
    /// near-duplicate request DTO purely to split route-bound from body-bound fields. Mirrors
    /// <c>VehiclesController.Update</c>'s exact shape.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var dto = await sender.Send(command with { CustomerId = id }, cancellationToken);

        return Ok(dto);
    }

    /// <summary>
    /// Permanently, irreversibly removes the customer -- the app's first real hard delete (Story
    /// 3.3), unlike Vehicle's <c>POST .../deactivate</c>. A genuine <c>DELETE</c> verb, anticipated
    /// by Story 2.3's Design Notes ("keeps [HttpDelete] free in case Customers ever need genuine
    /// verb-based REST semantics for something different"). Blocked with a 409 (mapped by
    /// <see cref="ExceptionHandling.GlobalExceptionHandler"/>) if the customer has any bookings.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new HardDeleteCustomerCommand(id), cancellationToken);

        return NoContent();
    }
}
