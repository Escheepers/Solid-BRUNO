using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers.Dtos;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="UpdateCustomerCommand"/>: <see cref="ICustomerRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the customer doesn't exist or isn't currently
/// visible) -> a duplicate-Email existence check excluding the customer's own id -> the domain
/// mutation (which itself still enforces every invariant, letting
/// <see cref="DomainRuleViolationException"/> propagate unmodified) -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <see cref="CustomerDto"/>. Contains no
/// validation logic of its own -- input-shape rules live in
/// <see cref="UpdateCustomerCommandValidator"/> (via the pipeline), domain invariants live in
/// <see cref="Customer.Update"/>. Mirrors <c>UpdateVehicleCommandHandler</c>'s exact shape.
/// </summary>
public class UpdateCustomerCommandHandler(ICustomerRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCustomerCommand, CustomerDto>
{
    public async Task<CustomerDto> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        var duplicateExists = await repository.ExistsByEmailAsync(
            request.Email, request.CustomerId, cancellationToken);

        if (duplicateExists)
        {
            throw new DomainRuleViolationException(
                "Customer", "Email", "This email address is already in use.");
        }

        customer.Update(request.FirstName, request.LastName, request.Email, request.PhoneNumber);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CustomerDto.FromDomain(customer);
    }
}
