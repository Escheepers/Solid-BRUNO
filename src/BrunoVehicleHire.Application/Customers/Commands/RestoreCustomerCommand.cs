using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Requests that the Customer identified by <see cref="CustomerId"/> be restored
/// (<see cref="Domain.Customer.Restore"/>). Returns <c>Unit</c> (MediatR's empty-response type),
/// not a DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/Delete commands... return
/// <c>Unit</c>/204"); Restore is the same shape of mutation. Mirrors <c>RestoreVehicleCommand</c>
/// exactly.
/// </summary>
public record RestoreCustomerCommand(Guid CustomerId) : IRequest;
