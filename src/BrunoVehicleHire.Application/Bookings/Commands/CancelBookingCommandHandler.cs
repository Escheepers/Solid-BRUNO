using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Orchestrates <see cref="CancelBookingCommand"/>: <see cref="IBookingRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the booking doesn't exist) -> <see cref="Booking.Cancel"/>
/// -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>, wrapped so a
/// <see cref="DbUpdateConcurrencyException"/> from the xmin concurrency token (two near-simultaneous
/// requests both read the pre-mutation row) re-fetches the now-current row and re-invokes
/// <see cref="Booking.Cancel"/>, letting IT throw the correct exception for the true current state
/// rather than silently double-processing. Contains no business logic of its own beyond that
/// orchestration (SRP) -- every ineligibility rule (already-Cancelled, already-Completed,
/// past-EndDate-still-Active) lives entirely in <see cref="Booking.Cancel"/> and propagates as
/// <see cref="Domain.Exceptions.DomainRuleViolationException"/>, mapped to a 409 by
/// <see cref="Api.ExceptionHandling.GlobalExceptionHandler"/>. Mirrors
/// <see cref="Vehicles.Commands.SoftDeleteVehicleCommandHandler"/>'s exact shape.
/// </summary>
public class CancelBookingCommandHandler(IBookingRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CancelBookingCommand>
{
    public async Task Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await repository.GetByIdAsync(request.BookingId, cancellationToken)
            ?? throw new NotFoundException(nameof(Booking), request.BookingId);

        booking.Cancel();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rare race this catch exists for: two near-simultaneous requests (e.g. a
            // double-click) both read the pre-mutation row before either committed. Re-fetching and
            // re-calling Cancel() lets the domain method see the true post-mutation state and throw
            // its own correct DomainRuleViolationException -- never a hand-written duplicate message.
            var current = await repository.GetByIdAsync(request.BookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(Booking), request.BookingId);

            current.Cancel();

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
