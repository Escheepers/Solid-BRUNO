namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// A command that carries a booking's date range, so <see cref="BookingDatesValidator{T}"/> can hold
/// the one definition of the date rules for both create and edit.
/// </summary>
public interface IBookingDates
{
    DateOnly StartDate { get; }

    DateOnly EndDate { get; }
}
