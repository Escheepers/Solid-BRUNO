using BrunoVehicleHire.Application.Bookings.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Requests creation of a new Booking. Deliberately carries no <c>TotalPrice</c> field -- it is
/// always computed server-side in <see cref="CreateBookingCommandHandler"/> from the Vehicle's
/// <c>DailyRate</c> and the requested date range, never trusted from the request (spec-4-1's
/// Boundaries). Returns the created <see cref="BookingDto"/> -- never a bare id (AD-2). Validated by
/// <see cref="CreateBookingCommandValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler.
/// </summary>
public record CreateBookingCommand(
    Guid VehicleId,
    Guid CustomerId,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<BookingDto>;
