using BrunoVehicleHire.Application.Customers.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Requests creation of a new Customer. Returns the created <see cref="CustomerDto"/> -- never a
/// bare id (AD-2). Validated by <see cref="CreateCustomerCommandValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler. Mirrors <c>CreateVehicleCommand</c>'s exact shape.
/// </summary>
public record CreateCustomerCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber) : IRequest<CustomerDto>;
