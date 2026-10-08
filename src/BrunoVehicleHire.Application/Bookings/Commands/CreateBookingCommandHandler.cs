using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Orchestrates <see cref="CreateBookingCommand"/>: <see cref="IVehicleRepository.GetByIdIncludingSoftDeletedAsync"/>
/// (throwing <see cref="NotFoundException"/> if no row exists for that id at all, or a
/// <see cref="DomainRuleViolationException"/>/409 if the row exists but is soft-deleted -- spec-4-1's
/// Scope decision 2) -> <see cref="ICustomerRepository.GetByIdAsync"/> (the existing FILTERED lookup,
/// so an inactive/anonymized customer 404s exactly like any other nonexistent customer -- Scope
/// decision 2's deliberate asymmetry from Vehicle) -> <see cref="BookingOverlapGuard"/> (spec-4-2's
/// AD-7 layer-1 application check: fetches the vehicle's non-Cancelled bookings and calls the real
/// <see cref="DateRange.Overlaps"/> on each in plain C#) -> <c>TotalPrice = Vehicle.DailyRate x
/// (EndDate - StartDate in days)</c> computed here, never trusted from the request ->
/// <see cref="Booking.Create"/> (called exactly as it already exists, unmodified) ->
/// <see cref="IBookingRepository.AddAsync"/> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>,
/// wrapped so the rare race AD-7's layer-2 database constraint catches (both requests passing the
/// layer-1 check before either commits) is translated back into the identical 409 shape -> mapped
/// <see cref="BookingDto"/>. Contains no validation logic of its own -- input-shape rules (EndDate
/// &gt; StartDate) live in <see cref="CreateBookingCommandValidator"/> (via the pipeline); the same
/// invariant additionally stays enforced, unmodified, inside <see cref="Booking.Create"/> itself as
/// defense-in-depth.
/// </summary>
public class CreateBookingCommandHandler(
    IVehicleRepository vehicleRepository,
    ICustomerRepository customerRepository,
    IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateBookingCommand, BookingDto>
{
    /// <summary>
    /// Postgres' SQLSTATE for a violated <c>EXCLUDE</c> constraint -- this story's own migration adds
    /// exactly one such constraint (the overlap backstop), so seeing this code on <c>SaveChangesAsync</c>
    /// unambiguously means the race this story exists to guard against just happened.
    /// </summary>
    private const string ExclusionViolationSqlState = "23P01";

    public async Task<BookingDto> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await vehicleRepository.GetByIdIncludingSoftDeletedAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        if (vehicle.IsDeleted)
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(Vehicle.IsDeleted), "This vehicle is not available.");
        }

        var customer = await customerRepository.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        var requestedRange = new DateRange(request.StartDate, request.EndDate);
        await BookingOverlapGuard.EnsureNoOverlapAsync(
            bookingRepository, vehicle.Id, requestedRange, excludingBookingId: null, cancellationToken);

        var totalPrice = vehicle.DailyRate * (request.EndDate.DayNumber - request.StartDate.DayNumber);

        var booking = Booking.Create(vehicle.Id, customer.Id, request.StartDate, request.EndDate, totalPrice);

        await bookingRepository.AddAsync(booking, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ExclusionViolationSqlState })
        {
            // The rare race AD-7's layer 2 exists for: two concurrent requests both passed
            // EnsureNoOverlapAsync's in-memory check above before either committed. Re-running the
            // identical overlap lookup makes this 409 byte-identical in shape to the layer-1 path
            // above (spec-4-2's Scope decision 3), never a generic "conflict occurred" 500-avoidant
            // fallback. EnsureNoOverlapAsync always throws here -- the conflicting row that made
            // Postgres reject the insert is, by definition, still there to be found again.
            await BookingOverlapGuard.EnsureNoOverlapAsync(
                bookingRepository, vehicle.Id, requestedRange, excludingBookingId: null, cancellationToken);
            throw;
        }

        return BookingDto.FromDomain(booking, vehicle, customer);
    }
}
