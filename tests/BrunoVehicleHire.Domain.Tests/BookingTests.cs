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
}
