using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="SoftDeleteCustomerCommand"/>: <see cref="ICustomerRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the customer doesn't exist or isn't currently
/// visible -- including an already-soft-deleted customer, since the query filter still applies) ->
/// <see cref="Customer.SoftDelete"/> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// Contains no business logic of its own beyond that orchestration (SRP) -- deliberately does NOT
/// check booking count (spec-3-4's Scope decision 2: Deactivate is not gated on bookings, unlike
/// <c>HardDeleteCustomerCommandHandler</c>). Mirrors <c>SoftDeleteVehicleCommandHandler</c> exactly.
/// </summary>
public class SoftDeleteCustomerCommandHandler(ICustomerRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<SoftDeleteCustomerCommand>
{
    public async Task Handle(SoftDeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        customer.SoftDelete();

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
