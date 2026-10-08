using System.Globalization;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// The one definition of "this vehicle is already booked then", shared by
/// <see cref="CreateBookingCommandHandler"/> and <see cref="UpdateBookingCommandHandler"/> so the rule
/// and its exact message can never drift apart. Throws <see cref="DomainRuleViolationException"/> for the
/// first non-Cancelled booking on the vehicle whose own range overlaps <paramref name="requestedRange"/>
/// (half-open semantics, so a same-day turnover never conflicts), naming that conflicting booking's
/// dates -- never the requested ones. <paramref name="excludingBookingId"/> skips one booking, so an
/// edit is not blocked by the very booking it is moving.
/// </summary>
internal static class BookingOverlapGuard
{
    public static async Task EnsureNoOverlapAsync(
        IBookingRepository bookingRepository,
        Guid vehicleId,
        DateRange requestedRange,
        Guid? excludingBookingId,
        CancellationToken cancellationToken)
    {
        var existingBookings = await bookingRepository.GetNonCancelledForVehicleAsync(vehicleId, cancellationToken);

        foreach (var existing in existingBookings)
        {
            if (existing.Id == excludingBookingId)
            {
                continue;
            }

            var existingRange = new DateRange(existing.StartDate, existing.EndDate);
            if (requestedRange.Overlaps(existingRange))
            {
                throw new DomainRuleViolationException(
                    nameof(Booking),
                    "Overlap",
                    $"This vehicle is already booked {FormatDate(existing.StartDate)} – {FormatDate(existing.EndDate)}");
            }
        }
    }

    /// <summary>E.g. 2 Sep -- invariant-culture, day without a leading zero, per the AC's own example.</summary>
    private static string FormatDate(DateOnly date) => date.ToString("d MMM", CultureInfo.InvariantCulture);
}
