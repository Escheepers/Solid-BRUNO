using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="SoftDeleteCustomerCommand"/>: <see cref="ICustomerRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the customer doesn't exist or isn't currently
/// visible -- including an already-soft-deleted customer, since the query filter still applies) ->
/// <see cref="CustomerActiveBookingGuard"/> (409 if the customer has an Active booking --
/// spec-customer-deactivate-erase-blocked-by-active-bookings, which deliberately reverses spec-3-4's
/// original "Deactivate is not gated on bookings" and mirrors <c>SoftDeleteVehicleCommandHandler</c>;
/// Completed/Cancelled history never blocks) -> <see cref="Customer.SoftDelete"/> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own beyond that
/// orchestration (SRP).
/// </summary>
public class SoftDeleteCustomerCommandHandler(
    ICustomerRepository repository,
    IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SoftDeleteCustomerCommand>
{
    public async Task Handle(SoftDeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        await CustomerActiveBookingGuard.EnsureNoActiveBookingAsync(
            bookingRepository, customer.Id, cancellationToken);

        customer.SoftDelete();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // See ConcurrencyConflict: a concurrent deactivate makes the filtered re-read find nothing (404).
            throw await ConcurrencyConflict.ResolveAsync(
                nameof(Customer), request.CustomerId, () => repository.GetByIdAsync(request.CustomerId, cancellationToken));
        }
    }
}
