using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers.Dtos;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="CreateCustomerCommand"/>: a pre-insert duplicate-Email existence check
/// (mirrors <c>CreateVehicleCommandHandler</c>'s duplicate-RegistrationNumber check) -> the domain
/// factory -> <see cref="ICustomerRepository.AddAsync"/> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <see cref="CustomerDto"/>. Contains no
/// validation logic of its own -- input-shape rules live in
/// <see cref="CreateCustomerCommandValidator"/> (via the pipeline), domain invariants live in
/// <see cref="Customer.Create"/>. This handler only orchestrates: it never computes an EmailHash,
/// never touches <c>IDataProtector</c> -- both are entirely <c>AppDbContext</c>'s concern
/// (Infrastructure), reached only through <see cref="ICustomerRepository"/>'s abstraction.
/// </summary>
public class CreateCustomerCommandHandler(ICustomerRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var alreadyExists = await repository.ExistsByEmailAsync(
            request.Email, excludingId: null, cancellationToken);

        if (alreadyExists)
        {
            throw new DomainRuleViolationException(
                "Customer", "Email", "This email address is already in use.");
        }

        var customer = Customer.Create(
            request.FirstName, request.LastName, request.Email, request.PhoneNumber);

        await repository.AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CustomerDto.FromDomain(customer);
    }
}
