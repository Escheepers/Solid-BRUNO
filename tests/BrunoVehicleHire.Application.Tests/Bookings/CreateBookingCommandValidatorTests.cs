using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Domain;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Boundary-case coverage for <see cref="CreateBookingCommandValidator"/>: <c>EndDate</c> must be
/// strictly after <c>StartDate</c> (spec-4-1's Scope decision 1 -- a 400-level shape check that
/// deliberately duplicates <c>Booking.Create</c>'s own domain invariant). VehicleId/CustomerId are
/// deliberately never asserted here -- an unknown/inactive reference is a 404/409 concern for
/// <see cref="CreateBookingCommandHandler"/>, not a shape check.
/// </summary>
public class CreateBookingCommandValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private readonly CreateBookingCommandValidator _validator = new(
        new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 23, 59, 0, TimeSpan.Zero)));

    private static CreateBookingCommand ValidCommand(
        DateOnly? startDate = null,
        DateOnly? endDate = null) => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startDate ?? new DateOnly(2026, 10, 1),
            endDate ?? new DateOnly(2026, 10, 5));

    [Fact]
    public void EndDate_AfterStartDate_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.EndDate);
    }

    [Fact]
    public void EndDate_EqualToStartDate_FailsValidation()
    {
        var startDate = new DateOnly(2026, 10, 1);

        var result = _validator.TestValidate(ValidCommand(startDate: startDate, endDate: startDate));

        result.ShouldHaveValidationErrorFor(c => c.EndDate);
    }

    [Fact]
    public void EndDate_BeforeStartDate_FailsValidation()
    {
        var startDate = new DateOnly(2026, 10, 1);

        var result = _validator.TestValidate(
            ValidCommand(startDate: startDate, endDate: startDate.AddDays(-1)));

        result.ShouldHaveValidationErrorFor(c => c.EndDate);
    }

    [Fact]
    public void StartDate_Today_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand(startDate: Today, endDate: Today.AddDays(2)));

        result.ShouldNotHaveValidationErrorFor(c => c.StartDate);
    }

    [Fact]
    public void StartDate_Yesterday_FailsValidation()
    {
        var result = _validator.TestValidate(
            ValidCommand(startDate: Today.AddDays(-1), endDate: Today.AddDays(2)));

        result.ShouldHaveValidationErrorFor(c => c.StartDate)
            .WithErrorMessage("StartDate cannot be in the past.");
    }

    [Fact]
    public void EndDate_ExactlyTheMaximumDurationAfterStart_PassesValidation()
    {
        var result = _validator.TestValidate(
            ValidCommand(startDate: Today, endDate: Today.AddDays(Booking.MaxDurationDays)));

        result.ShouldNotHaveValidationErrorFor(c => c.EndDate);
    }

    [Fact]
    public void EndDate_MoreThanTheMaximumDurationAfterStart_FailsValidation()
    {
        var result = _validator.TestValidate(
            ValidCommand(startDate: Today, endDate: Today.AddDays(Booking.MaxDurationDays + 1)));

        result.ShouldHaveValidationErrorFor(c => c.EndDate);
    }

    /// <summary>Minimal fixed-clock test double -- mirrors <c>CancelBookingCommandHandlerTests</c>'s own.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
