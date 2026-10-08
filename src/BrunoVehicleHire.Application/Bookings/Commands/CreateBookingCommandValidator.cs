namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="CreateBookingCommand"/> -- the date rules (no start in the
/// past, <c>EndDate</c> after <c>StartDate</c>, maximum length) all live in
/// <see cref="BookingDatesValidator{T}"/>, shared with <see cref="UpdateBookingCommandValidator"/>.
/// VehicleId/CustomerId are deliberately never checked here -- an unknown/inactive reference is a
/// 404/409 concern for <see cref="CreateBookingCommandHandler"/>, not a shape check. Run through the
/// shared pipeline -- never invoked manually by the handler.
/// </summary>
public class CreateBookingCommandValidator(TimeProvider timeProvider)
    : BookingDatesValidator<CreateBookingCommand>(timeProvider);
