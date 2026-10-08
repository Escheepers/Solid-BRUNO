using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="RestoreCustomerCommand"/>:
/// <see cref="ICustomerRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="Customer.Restore"/>,
/// letting <see cref="Domain.Exceptions.DomainRuleViolationException"/> propagate unmodified when the
/// customer is already active -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no
/// business logic of its own beyond that orchestration (SRP) -- deliberately uses
/// <c>GetByIdIncludingSoftDeletedAsync</c>, not <c>GetByIdAsync</c>, since the latter's query filter
/// would incorrectly 404 every soft-deleted customer this command is asked to restore. Mirrors
/// <c>RestoreVehicleCommandHandler</c> exactly.
/// </summary>
public class RestoreCustomerCommandHandler(ICustomerRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<RestoreCustomerCommand>
{
    public async Task Handle(RestoreCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repository.GetByIdIncludingSoftDeletedAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        customer.Restore();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Mirrors RestoreVehicleCommandHandler: a near-simultaneous second Restore read the
            // pre-mutation row too. Re-fetching and re-calling Restore() lets the domain method report
            // the true current state ("Already active.") instead of silently double-processing.
            var current = await repository.GetByIdIncludingSoftDeletedAsync(request.CustomerId, cancellationToken)
                ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

            current.Restore();

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
