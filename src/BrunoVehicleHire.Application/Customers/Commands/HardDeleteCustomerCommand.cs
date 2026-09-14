using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Requests that the Customer identified by <see cref="CustomerId"/> be permanently, irreversibly
/// removed (<see cref="ICustomerRepository.RemoveAsync"/>) -- the app's first genuine hard-delete
/// (Story 3.3), unlike Vehicle's soft-delete. Returns <c>Unit</c> (MediatR's empty-response type),
/// not a DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/Delete commands... return
/// <c>Unit</c>/204"). Mirrors <c>SoftDeleteVehicleCommand</c>'s exact shape.
/// </summary>
public record HardDeleteCustomerCommand(Guid CustomerId) : IRequest;
