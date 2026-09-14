using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Orchestrates <see cref="CancelBookingCommand"/>: <see cref="IBookingRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the booking doesn't exist) -> <see cref="Booking.Cancel"/>
/// -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own
/// beyond that orchestration (SRP) -- every ineligibility rule (already-Cancelled, already-Completed,
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

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
