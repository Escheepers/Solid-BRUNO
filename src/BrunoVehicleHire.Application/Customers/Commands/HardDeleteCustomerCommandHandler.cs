using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="HardDeleteCustomerCommand"/>: <see cref="ICustomerRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the customer doesn't exist or isn't currently
/// visible) -&gt; <see cref="IBookingRepository.ExistsForCustomerAsync"/> (throwing
/// <see cref="DomainRuleViolationException"/> if any booking references this customer, naming both
/// alternatives per the AC) -&gt; <see cref="ICustomerRepository.RemoveAsync"/> -&gt; exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own beyond that
/// orchestration (SRP) -- mirrors <c>SoftDeleteVehicleCommandHandler</c>'s exact shape, with one
/// extra guard step.
/// </summary>
public class HardDeleteCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<HardDeleteCustomerCommand>
{
    public async Task Handle(HardDeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        var hasBookings = await bookingRepository.ExistsForCustomerAsync(customer.Id, cancellationToken);

        if (hasBookings)
        {
            // An Active booking also blocks deactivate/erase, so pointing the user at those would be
            // misleading -- tell them to cancel it first (the same message those two actions show).
            await CustomerActiveBookingGuard.EnsureNoActiveBookingAsync(
                bookingRepository, customer.Id, cancellationToken);

            throw new DomainRuleViolationException(
                "Customer", "HasBookings", "This customer has bookings — deactivate or erase their data instead.");
        }

        await customerRepository.RemoveAsync(customer, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // See ConcurrencyConflict: if the other request deleted it the re-read finds nothing (404).
            throw await ConcurrencyConflict.ResolveAsync(
                nameof(Customer), request.CustomerId,
                () => customerRepository.GetByIdAsync(request.CustomerId, cancellationToken));
        }
    }
}
