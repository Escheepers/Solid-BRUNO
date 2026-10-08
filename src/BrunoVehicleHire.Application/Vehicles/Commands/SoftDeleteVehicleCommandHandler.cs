using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="SoftDeleteVehicleCommand"/>: <see cref="IVehicleRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the vehicle doesn't exist or isn't currently
/// visible -- including an already-soft-deleted vehicle, since the query filter still applies) ->
/// <see cref="IBookingRepository.ExistsActiveForVehicleAsync"/> (throwing
/// <see cref="DomainRuleViolationException"/> if the vehicle has an Active booking --
/// spec-vehicle-deactivate-blocked-by-active-bookings, mirroring
/// <c>HardDeleteCustomerCommandHandler</c>'s exact shape) -> <see cref="Vehicle.SoftDelete"/> ->
/// exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no business logic of its own
/// beyond that orchestration (SRP) -- the domain method itself is idempotent, but this handler never
/// reaches it a second time for an already-deleted vehicle since <c>GetByIdAsync</c> already treats
/// it as not found.
/// </summary>
public class SoftDeleteVehicleCommandHandler(
    IVehicleRepository repository,
    IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SoftDeleteVehicleCommand>
{
    public async Task Handle(SoftDeleteVehicleCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await repository.GetByIdAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        var hasActiveBooking = await bookingRepository.ExistsActiveForVehicleAsync(vehicle.Id, cancellationToken);

        if (hasActiveBooking)
        {
            throw new DomainRuleViolationException(
                "Vehicle",
                "HasActiveBookings",
                "This vehicle has an active or upcoming booking — cancel it first, or wait for it to complete.");
        }

        vehicle.SoftDelete();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two near-simultaneous deactivates (e.g. a double-click) both read the active row; the
            // loser's xmin-guarded UPDATE matches nothing. Re-reading through the filtered lookup now
            // finds nothing (the winner deactivated it) -> the same 404 a sequential second click gets.
            throw await ConcurrencyConflict.ResolveAsync(
                nameof(Vehicle), request.VehicleId, () => repository.GetByIdAsync(request.VehicleId, cancellationToken));
        }
    }
}
