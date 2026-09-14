using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Requests that the Customer identified by <see cref="CustomerId"/> have their personal data
/// irreversibly erased (<see cref="Domain.Customer.Anonymize"/>). Returns <c>Unit</c> (MediatR's
/// empty-response type), not a DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/
/// Delete commands... return <c>Unit</c>/204"); mirrors <c>SoftDeleteCustomerCommand</c>/
/// <c>RestoreCustomerCommand</c> exactly.
/// </summary>
public record AnonymizeCustomerCommand(Guid CustomerId) : IRequest;
