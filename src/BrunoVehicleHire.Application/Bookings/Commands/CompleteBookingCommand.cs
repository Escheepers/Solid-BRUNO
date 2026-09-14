using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Requests that the Booking identified by <see cref="BookingId"/> be completed
/// (<see cref="Domain.Booking.Complete"/>). Has no HTTP endpoint anywhere -- <c>BookingsController</c>
/// gains nothing from this story (spec-4-4's Scope decision 3): <c>BookingCompletionSweepService</c>
/// is its only caller, dispatched internally via <c>ISender</c>, matching AD-17's rule that Completed
/// is never directly settable by a user. Returns <c>Unit</c> (MediatR's empty-response type), not a
/// DTO -- per AD-2's exception list, mirroring <see cref="CancelBookingCommand"/>'s exact shape.
/// </summary>
public record CompleteBookingCommand(Guid BookingId) : IRequest;
