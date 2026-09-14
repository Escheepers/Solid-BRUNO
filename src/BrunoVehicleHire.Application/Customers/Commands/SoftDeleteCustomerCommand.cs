using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Requests that the Customer identified by <see cref="CustomerId"/> be soft-deleted
/// ("deactivated" -- <see cref="Domain.Customer.SoftDelete"/>). Returns <c>Unit</c> (MediatR's
/// empty-response type), not a DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/
/// Delete commands... return <c>Unit</c>/204"); mirrors <c>SoftDeleteVehicleCommand</c> exactly.
/// </summary>
public record SoftDeleteCustomerCommand(Guid CustomerId) : IRequest;
