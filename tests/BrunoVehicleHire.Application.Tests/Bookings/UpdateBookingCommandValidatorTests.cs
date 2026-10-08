using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Domain;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// <see cref="UpdateBookingCommandValidator"/> shares <c>BookingDatesValidator</c> with create, so these
/// prove the same date rules apply to an edit: no start in the past, end after start, maximum length.
/// </summary>
public class UpdateBookingCommandValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private readonly UpdateBookingCommandValidator _validator = new(
        new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 23, 59, 0, TimeSpan.Zero)));

    private static UpdateBookingCommand Command(DateOnly start, DateOnly end) => new(Guid.NewGuid(), start, end);

    [Fact]
    public void ValidFutureRange_Passes()
    {
        var result = _validator.TestValidate(Command(Today.AddDays(5), Today.AddDays(8)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void StartToday_Passes()
    {
        var result = _validator.TestValidate(Command(Today, Today.AddDays(2)));

        result.ShouldNotHaveValidationErrorFor(c => c.StartDate);
    }

    [Fact]
    public void StartYesterday_Fails()
    {
        var result = _validator.TestValidate(Command(Today.AddDays(-1), Today.AddDays(2)));

        result.ShouldHaveValidationErrorFor(c => c.StartDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EndNotAfterStart_Fails(int daysAfterStart)
    {
        var start = Today.AddDays(5);

        var result = _validator.TestValidate(Command(start, start.AddDays(daysAfterStart)));

        result.ShouldHaveValidationErrorFor(c => c.EndDate);
    }

    [Fact]
    public void LongerThanTheMaximumDuration_Fails()
    {
        var result = _validator.TestValidate(
            Command(Today, Today.AddDays(Booking.MaxDurationDays + 1)));

        result.ShouldHaveValidationErrorFor(c => c.EndDate);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
