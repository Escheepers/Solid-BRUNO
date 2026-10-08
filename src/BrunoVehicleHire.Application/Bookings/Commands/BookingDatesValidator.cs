using BrunoVehicleHire.Domain;
using FluentValidation;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// The one definition of the input-shape rules for a booking's date range, shared by
/// <see cref="CreateBookingCommandValidator"/> and <see cref="UpdateBookingCommandValidator"/> so the
/// two can never drift apart: <c>StartDate</c> must not be before today (UTC, the same clock
/// <c>Booking.Cancel</c> and the completion sweep use; today itself is allowed), <c>EndDate</c> must be
/// strictly after <c>StartDate</c>, and the range may span at most <see cref="Booking.MaxDurationDays"/>
/// days. These duplicate <c>Booking</c>'s own domain invariants at the boundary so a bad range is a
/// plain 400 via the shared pipeline (spec-4-1's Scope decision 1); the domain keeps enforcing them
/// as defense-in-depth. The clock is the injected <see cref="TimeProvider"/> so tests can fix it.
/// </summary>
public abstract class BookingDatesValidator<T> : AbstractValidator<T>
    where T : IBookingDates
{
    protected BookingDatesValidator(TimeProvider timeProvider)
    {
        RuleFor(c => c.StartDate)
            .Must(startDate => startDate >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("StartDate cannot be in the past.");

        RuleFor(c => c.EndDate)
            .GreaterThan(c => c.StartDate)
            .WithMessage("EndDate must be after StartDate.");

        RuleFor(c => c.EndDate)
            .Must((c, endDate) => endDate.DayNumber - c.StartDate.DayNumber <= Booking.MaxDurationDays)
            .WithMessage($"A booking cannot be longer than {Booking.MaxDurationDays} days.");
    }
}
