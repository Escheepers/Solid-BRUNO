using BrunoVehicleHire.Application.Bookings.Commands;
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
    private readonly CreateBookingCommandValidator _validator = new();

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
}
