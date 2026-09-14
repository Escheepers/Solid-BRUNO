using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Orchestrates <see cref="CreateBookingCommand"/>: <see cref="IVehicleRepository.GetByIdIncludingSoftDeletedAsync"/>
/// (throwing <see cref="NotFoundException"/> if no row exists for that id at all, or a
/// <see cref="DomainRuleViolationException"/>/409 if the row exists but is soft-deleted -- spec-4-1's
/// Scope decision 2) -> <see cref="ICustomerRepository.GetByIdAsync"/> (the existing FILTERED lookup,
/// so an inactive/anonymized customer 404s exactly like any other nonexistent customer -- Scope
/// decision 2's deliberate asymmetry from Vehicle) -> <c>TotalPrice = Vehicle.DailyRate x (EndDate -
/// StartDate in days)</c> computed here, never trusted from the request -> <see cref="Booking.Create"/>
/// (called exactly as it already exists, unmodified) -> <see cref="IBookingRepository.AddAsync"/> ->
/// exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <see cref="BookingDto"/>. Contains
/// no validation logic of its own -- input-shape rules (EndDate &gt; StartDate) live in
/// <see cref="CreateBookingCommandValidator"/> (via the pipeline); the same invariant additionally
/// stays enforced, unmodified, inside <see cref="Booking.Create"/> itself as defense-in-depth.
/// </summary>
public class CreateBookingCommandHandler(
    IVehicleRepository vehicleRepository,
    ICustomerRepository customerRepository,
    IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateBookingCommand, BookingDto>
{
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

        var totalPrice = vehicle.DailyRate * (request.EndDate.DayNumber - request.StartDate.DayNumber);

        var booking = Booking.Create(vehicle.Id, customer.Id, request.StartDate, request.EndDate, totalPrice);

        await bookingRepository.AddAsync(booking, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BookingDto.FromDomain(booking, vehicle, customer);
    }
}
