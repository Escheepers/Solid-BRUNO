using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Requests that the Booking identified by <see cref="BookingId"/> be cancelled
/// (<see cref="Domain.Booking.Cancel"/>). Returns <c>Unit</c> (MediatR's empty-response type), not a
/// DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/Delete commands... return
/// <c>Unit</c>/204"), mirroring <see cref="Vehicles.Commands.SoftDeleteVehicleCommand"/>'s exact shape.
/// </summary>
public record CancelBookingCommand(Guid BookingId) : IRequest;
