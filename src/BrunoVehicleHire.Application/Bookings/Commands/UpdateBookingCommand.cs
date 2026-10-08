using BrunoVehicleHire.Application.Bookings.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Requests that the booking identified by <see cref="BookingId"/> move to a new date range
/// (<see cref="Domain.Booking.Reschedule"/>) -- only an upcoming booking can be edited. Like
/// <see cref="CreateBookingCommand"/> it carries no <c>TotalPrice</c>: the price is always recomputed
/// server-side. <see cref="BookingId"/> comes from the route, never the body. Returns the updated
/// <see cref="BookingDto"/> (AD-2). Validated by <see cref="UpdateBookingCommandValidator"/>.
/// </summary>
public record UpdateBookingCommand(
    Guid BookingId,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<BookingDto>, IBookingDates;
