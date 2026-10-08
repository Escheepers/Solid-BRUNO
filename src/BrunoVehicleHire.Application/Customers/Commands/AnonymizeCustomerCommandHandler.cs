using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="AnonymizeCustomerCommand"/>:
/// <see cref="ICustomerRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="Customer.Anonymize"/>
/// -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own
/// beyond that orchestration (SRP) -- deliberately uses <c>GetByIdIncludingSoftDeletedAsync</c>, not
/// the filtered <c>GetByIdAsync</c>, so an already-deactivated customer can be erased directly
/// without first requiring Restore (spec-3-5's Scope decision 2). Blocked with a 409 only while the
/// customer has an ACTIVE booking (<see cref="CustomerActiveBookingGuard"/>,
/// spec-customer-deactivate-erase-blocked-by-active-bookings -- reversing spec-3-5's original "not
/// gated on bookings"); Completed/Cancelled history never blocks, and is exactly what Erase preserves.
/// Mirrors <c>RestoreCustomerCommandHandler</c>'s shape.
/// </summary>
public class AnonymizeCustomerCommandHandler(
    ICustomerRepository repository,
    IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AnonymizeCustomerCommand>
{
    public async Task Handle(AnonymizeCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repository.GetByIdIncludingSoftDeletedAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        // Only blocks actually PERFORMING the scrub: Erase is idempotent, so repeating it on an
        // already-anonymized customer (e.g. seed data that still holds an Active booking) stays a no-op.
        if (!customer.IsAnonymized)
        {
            await CustomerActiveBookingGuard.EnsureNoActiveBookingAsync(
                bookingRepository, customer.Id, cancellationToken);
        }

        customer.Anonymize();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Never report success unless the scrub was really saved: the other request might have been
            // an edit that put personal data back. Tell the caller to retry (a fresh request erases it).
            throw await ConcurrencyConflict.ResolveAsync(
                nameof(Customer), request.CustomerId,
                () => repository.GetByIdIncludingSoftDeletedAsync(request.CustomerId, cancellationToken));
        }
    }
}
