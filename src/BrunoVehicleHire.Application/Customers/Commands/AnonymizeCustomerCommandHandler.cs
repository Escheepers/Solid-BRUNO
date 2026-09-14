using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="AnonymizeCustomerCommand"/>:
/// <see cref="ICustomerRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="Customer.Anonymize"/>
/// -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own
/// beyond that orchestration (SRP) -- deliberately uses <c>GetByIdIncludingSoftDeletedAsync</c>, not
/// the filtered <c>GetByIdAsync</c>, so an already-deactivated customer can be erased directly
/// without first requiring Restore (spec-3-5's Scope decision 2). Deliberately does NOT check
/// booking count, mirroring spec-3-4's Scope decision 2 exactly (spec-3-5's Scope decision 1) --
/// epics.md's "customer with bookings" phrasing describes the realistic case, not an enforced
/// precondition. Mirrors <c>RestoreCustomerCommandHandler</c>'s exact shape.
/// </summary>
public class AnonymizeCustomerCommandHandler(ICustomerRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<AnonymizeCustomerCommand>
{
    public async Task Handle(AnonymizeCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repository.GetByIdIncludingSoftDeletedAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        customer.Anonymize();

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
