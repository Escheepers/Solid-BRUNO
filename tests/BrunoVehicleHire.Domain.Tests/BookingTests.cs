using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Domain.Tests;

/// <summary>
/// Covers every row of spec-3-3's Booking coverage: valid creation, the one invariant
/// <see cref="Booking.Create"/> enforces (EndDate strictly after StartDate), and the no-public-setters
/// reflection check. Written test-first: each of these failed (either "type/member does not exist" at
/// compile time, or a wrong-behavior assertion failure) before <c>Booking.Create</c> was implemented.
/// Mirrors <c>VehicleTests.cs</c>/<c>CustomerTests.cs</c>'s exact structure.
/// </summary>
public class BookingTests
{
    private static readonly Guid ValidVehicleId = Guid.CreateVersion7();
    private static readonly Guid ValidCustomerId = Guid.CreateVersion7();
    private static readonly DateOnly ValidStartDate = new(2026, 10, 1);
    private static readonly DateOnly ValidEndDate = new(2026, 10, 5);
    private const decimal ValidTotalPrice = 1500m;

    /// <summary>Minimal fixed-clock test double -- avoids adding a mocking package to Domain.Tests.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Booking CreateValidBooking(TimeProvider? timeProvider = null) =>
        Booking.Create(ValidVehicleId, ValidCustomerId, ValidStartDate, ValidEndDate, ValidTotalPrice, timeProvider);

    [Fact]
    public void Create_WithValidInput_ReturnsBookingWithExpectedDefaults()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        var booking = CreateValidBooking(timeProvider);

        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(ValidVehicleId, booking.VehicleId);
        Assert.Equal(ValidCustomerId, booking.CustomerId);
        Assert.Equal(ValidStartDate, booking.StartDate);
        Assert.Equal(ValidEndDate, booking.EndDate);
        Assert.Equal(ValidTotalPrice, booking.TotalPrice);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(fixedNow.UtcDateTime, booking.CreatedDate);
    }

    [Fact]
    public void Create_AssignsAVersion7Guid()
    {
        var booking = CreateValidBooking();

        // Version 7 UUIDs encode version "7" in the high nibble of byte 7.
        var versionNibble = booking.Id.ToByteArray()[7] >> 4;
        Assert.Equal(7, versionNibble);
    }

    [Fact]
    public void Create_WithEndDateEqualToStartDate_ThrowsDomainRuleViolationException()
    {
        var act = () => Booking.Create(
            ValidVehicleId, ValidCustomerId, ValidStartDate, ValidStartDate, ValidTotalPrice);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Fact]
    public void Create_WithEndDateBeforeStartDate_ThrowsDomainRuleViolationException()
    {
        var act = () => Booking.Create(
            ValidVehicleId, ValidCustomerId, ValidStartDate, ValidStartDate.AddDays(-1), ValidTotalPrice);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Fact]
    public void Booking_HasNoPublicSetters()
    {
        var propertiesWithPublicSetters = typeof(Booking)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    /// <summary>
    /// Covers spec-4-3's Booking coverage row, updated by
    /// spec-booking-form-error-handling-fixes.md's bugfix: <see cref="Booking.Cancel"/>'s success
    /// case, the already-Cancelled and already-Completed guard cases, and every now-ineligible
    /// "already started" case (mid-rental, past-both-dates-still-Active, and exactly-today) --
    /// all sharing the identical "has already started" message. Written test-first: every one of
    /// these failed (either "type/member does not exist" at compile time, or a wrong-message/
    /// wrong-eligibility assertion failure against the old <c>EndDate &lt;= today</c> rule) before
    /// <c>Booking.Cancel</c>'s corrected <c>StartDate &lt;= today</c> rule was implemented.
    /// </summary>
    public class Cancel
    {
        private static readonly DateOnly FutureStartDate = new(2026, 10, 1);
        private static readonly DateOnly FutureEndDate = new(2026, 10, 5);

        /// <summary>Fixes "today" at 2026-09-15 so FutureStartDate/FutureEndDate are both still ahead of it.</summary>
        private static readonly FixedTimeProvider TodayBeforeBooking =
            new(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));

        private static Booking ActiveFutureBooking() =>
            Booking.Create(ValidVehicleId, ValidCustomerId, FutureStartDate, FutureEndDate, ValidTotalPrice);

        [Fact]
        public void Cancel_ActiveFutureBooking_SetsStatusToCancelled()
        {
            var booking = ActiveFutureBooking();

            booking.Cancel(TodayBeforeBooking);

            Assert.Equal(BookingStatus.Cancelled, booking.Status);
        }

        [Fact]
        public void Cancel_AlreadyCancelledBooking_ThrowsWithExactMessage()
        {
            var booking = ActiveFutureBooking();
            booking.Cancel(TodayBeforeBooking);

            var act = () => booking.Cancel(TodayBeforeBooking);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot cancel — booking already cancelled.", exception.Message);
        }

        [Fact]
        public void Cancel_AlreadyCompletedBooking_ThrowsWithExactMessage()
        {
            // StartDate/EndDate are both still in the future -- only Status flips to Completed here,
            // proving the guard checks Status independently of either date.
            var booking = ActiveFutureBooking();
            typeof(Booking).GetProperty(nameof(Booking.Status))!
                .SetValue(booking, BookingStatus.Completed);

            var act = () => booking.Cancel(TodayBeforeBooking);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot cancel — booking has already started.", exception.Message);
        }

        [Fact]
        public void Cancel_MidRentalBooking_StartDateInPastEndDateInFuture_ThrowsAsAlreadyStarted()
        {
            // The bug this spec fixes: StartDate in the past (car already picked up) but EndDate
            // still in the future -- previously and incorrectly eligible under the old
            // EndDate <= today rule; now correctly rejected.
            var booking = Booking.Create(
                ValidVehicleId, ValidCustomerId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20), ValidTotalPrice);
            var midRentalToday = new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));

            var act = () => booking.Cancel(midRentalToday);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot cancel — booking has already started.", exception.Message);
        }

        [Fact]
        public void Cancel_StillActiveBookingWithBothDatesInPast_ThrowsAsAlreadyStarted()
        {
            // Both StartDate and EndDate have already passed relative to "today", but Status hasn't
            // been swept to Completed yet -- subsumed by (not merely coincidentally covered by) the
            // new StartDate <= today check, since StartDate is always strictly before EndDate.
            var booking = ActiveFutureBooking();
            var afterEndDate = new FixedTimeProvider(
                new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero));

            var act = () => booking.Cancel(afterEndDate);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot cancel — booking has already started.", exception.Message);
        }

        [Fact]
        public void Cancel_StillActiveBookingWhoseStartDateIsExactlyToday_ThrowsAsAlreadyStarted()
        {
            // StartDate on/before today is ineligible per the Boundaries -- exactly-today counts as
            // already started, the new rule's own boundary (replacing the old EndDate-exactly-today
            // boundary this test used to cover).
            var booking = ActiveFutureBooking();
            var onStartDate = new FixedTimeProvider(
                new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

            var act = () => booking.Cancel(onStartDate);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot cancel — booking has already started.", exception.Message);
        }
    }

    /// <summary>
    /// Covers spec-4-4's Booking coverage row: <see cref="Booking.Complete"/>'s success cases
    /// (EndDate today/past), the already-Completed idempotent no-op, the already-Cancelled throw, and
    /// the still-eligible-future-EndDate defense-in-depth throw. Written test-first: every one of
    /// these failed ("type/member does not exist" at compile time) before <c>Booking.Complete</c> was
    /// implemented.
    /// </summary>
    public class Complete
    {
        private static readonly DateOnly PastStartDate = new(2026, 9, 1);
        private static readonly DateOnly PastEndDate = new(2026, 9, 5);

        /// <summary>Fixes "today" at 2026-09-10 so PastStartDate/PastEndDate are both already over.</summary>
        private static readonly FixedTimeProvider TodayAfterBooking =
            new(new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero));

        private static Booking ActivePastBooking() =>
            Booking.Create(ValidVehicleId, ValidCustomerId, PastStartDate, PastEndDate, ValidTotalPrice);

        [Fact]
        public void Complete_ActiveBookingWithPastEndDate_SetsStatusToCompleted()
        {
            var booking = ActivePastBooking();

            booking.Complete(TodayAfterBooking);

            Assert.Equal(BookingStatus.Completed, booking.Status);
        }

        [Fact]
        public void Complete_ActiveBookingWhoseEndDateIsExactlyToday_SetsStatusToCompleted()
        {
            var booking = ActivePastBooking();
            var onEndDate = new FixedTimeProvider(new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero));

            booking.Complete(onEndDate);

            Assert.Equal(BookingStatus.Completed, booking.Status);
        }

        [Fact]
        public void Complete_AlreadyCompletedBooking_IsANoOp()
        {
            var booking = ActivePastBooking();
            booking.Complete(TodayAfterBooking);

            var act = () => booking.Complete(TodayAfterBooking);

            Assert.Null(Record.Exception(act));
            Assert.Equal(BookingStatus.Completed, booking.Status);
        }

        [Fact]
        public void Complete_AlreadyCancelledBooking_ThrowsWithExactMessage()
        {
            var booking = ActivePastBooking();
            typeof(Booking).GetProperty(nameof(Booking.Status))!
                .SetValue(booking, BookingStatus.Cancelled);

            var act = () => booking.Complete(TodayAfterBooking);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot complete — booking already cancelled.", exception.Message);
        }

        [Fact]
        public void Complete_StillActiveBookingWithFutureEndDate_ThrowsDefenseInDepth()
        {
            // EndDate hasn't passed yet -- Complete() must refuse even if somehow called, mirroring
            // Cancel()'s own defense-in-depth guard but on the complementary boundary.
            var booking = Booking.Create(
                ValidVehicleId, ValidCustomerId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), ValidTotalPrice);

            var act = () => booking.Complete(TodayAfterBooking);

            var exception = Assert.Throws<DomainRuleViolationException>(act);
            Assert.Equal("Cannot complete — booking has not ended yet.", exception.Message);
        }
    }
}
