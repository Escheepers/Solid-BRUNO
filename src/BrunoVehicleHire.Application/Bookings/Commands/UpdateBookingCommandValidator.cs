namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="UpdateBookingCommand"/>: exactly the date rules every new
/// booking gets, from <see cref="BookingDatesValidator{T}"/>. Whether the booking is still editable and
/// whether the new range clashes with another booking are handler/domain concerns (409), not shape.
/// </summary>
public class UpdateBookingCommandValidator(TimeProvider timeProvider)
    : BookingDatesValidator<UpdateBookingCommand>(timeProvider);
