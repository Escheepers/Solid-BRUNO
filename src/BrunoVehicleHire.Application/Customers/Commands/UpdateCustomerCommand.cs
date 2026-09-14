using BrunoVehicleHire.Application.Customers.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Requests an update to the existing Customer identified by <see cref="CustomerId"/>. Returns the
/// updated <see cref="CustomerDto"/> -- never a bare id (AD-2: every mutating Command that
/// creates/updates an aggregate returns its DTO). Validated by
/// <see cref="UpdateCustomerCommandValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler. The API's <c>PUT</c> controller action always overrides
/// <see cref="CustomerId"/> from the route segment, never trusting a client-supplied body value for
/// it. Mirrors <c>UpdateVehicleCommand</c>'s exact shape.
/// </summary>
public record UpdateCustomerCommand(
    Guid CustomerId,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber) : IRequest<CustomerDto>;
