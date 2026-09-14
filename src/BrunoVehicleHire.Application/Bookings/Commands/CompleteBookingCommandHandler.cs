using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Orchestrates <see cref="CompleteBookingCommand"/>: <see cref="IBookingRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the booking doesn't exist) -> <see cref="Booking.Complete"/>
/// -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own
/// beyond that orchestration (SRP) -- every eligibility rule (already-Cancelled, still-eligible-future-
/// EndDate) lives entirely in <see cref="Booking.Complete"/> and propagates as
/// <see cref="Domain.Exceptions.DomainRuleViolationException"/>. <c>BookingCompletionSweepService</c>
/// is this command's only dispatcher (spec-4-4's Scope decision 3) -- mirrors
/// <see cref="CancelBookingCommandHandler"/>'s exact shape.
/// </summary>
public class CompleteBookingCommandHandler(IBookingRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CompleteBookingCommand>
{
    public async Task Handle(CompleteBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await repository.GetByIdAsync(request.BookingId, cancellationToken)
            ?? throw new NotFoundException(nameof(Booking), request.BookingId);

        booking.Complete();

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
