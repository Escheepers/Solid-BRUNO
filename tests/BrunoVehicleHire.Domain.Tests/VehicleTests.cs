using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Domain.Tests;

/// <summary>
/// Covers every row of the Story 1.3 I/O matrix for <see cref="Vehicle"/>.
/// Written test-first: each of these failed (either "type/member does not exist" at
/// compile time, or a wrong-behavior assertion failure) before <c>Vehicle.Create</c>/
/// <c>SoftDelete</c> were implemented.
/// </summary>
public class VehicleTests
{
    private const string ValidRegistrationNumber = "CA123456";
    private const string ValidMake = "Toyota";
    private const string ValidModel = "Corolla";
    private const int ValidYear = 2023;
    private const decimal ValidDailyRate = 350m;

    /// <summary>Minimal fixed-clock test double -- avoids adding a mocking package to Domain.Tests.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Vehicle CreateValidVehicle(TimeProvider? timeProvider = null) =>
        Vehicle.Create(ValidRegistrationNumber, ValidMake, ValidModel, ValidYear, ValidDailyRate, timeProvider);

    [Fact]
    public void Create_WithValidInput_ReturnsVehicleWithExpectedDefaults()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        var vehicle = CreateValidVehicle(timeProvider);

        Assert.NotEqual(Guid.Empty, vehicle.Id);
        Assert.False(vehicle.IsDeleted);
        Assert.Equal(fixedNow.UtcDateTime, vehicle.CreatedDate);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Fact]
    public void Create_AssignsAVersion7Guid()
    {
        var vehicle = CreateValidVehicle();

        // Version 7 UUIDs encode version "7" in the high nibble of byte 7.
        var versionNibble = vehicle.Id.ToByteArray()[7] >> 4;
        Assert.Equal(7, versionNibble);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankRegistrationNumber_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Vehicle.Create(blank, ValidMake, ValidModel, ValidYear, ValidDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankMake_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Vehicle.Create(ValidRegistrationNumber, blank, ValidModel, ValidYear, ValidDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankModel_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Vehicle.Create(ValidRegistrationNumber, ValidMake, blank, ValidYear, ValidDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-350.50)]
    public void Create_WithNonPositiveDailyRate_ThrowsDomainRuleViolationException(decimal nonPositiveRate)
    {
        var act = () => Vehicle.Create(ValidRegistrationNumber, ValidMake, ValidModel, ValidYear, nonPositiveRate);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Fact]
    public void Create_WithYearBefore1900_ThrowsDomainRuleViolationException()
    {
        var act = () => Vehicle.Create(ValidRegistrationNumber, ValidMake, ValidModel, 1899, ValidDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Fact]
    public void Create_WithYearTwoYearsFromNow_ThrowsDomainRuleViolationException()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        var act = () => Vehicle.Create(
            ValidRegistrationNumber, ValidMake, ValidModel, fixedNow.Year + 2, ValidDailyRate, timeProvider);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Fact]
    public void Create_WithYearEqualToNextCalendarYear_Succeeds()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        var vehicle = Vehicle.Create(
            ValidRegistrationNumber, ValidMake, ValidModel, fixedNow.Year + 1, ValidDailyRate, timeProvider);

        Assert.Equal(fixedNow.Year + 1, vehicle.Year);
    }

    private const string UpdatedRegistrationNumber = "CA654321";
    private const string UpdatedMake = "Honda";
    private const string UpdatedModel = "Civic";
    private const int UpdatedYear = 2024;
    private const decimal UpdatedDailyRate = 400m;

    [Fact]
    public void Update_WithValidInput_ChangesPropertiesAndLeavesIdCreatedDateAndIsDeletedUnchanged()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var vehicle = CreateValidVehicle(timeProvider);
        var originalId = vehicle.Id;
        var originalCreatedDate = vehicle.CreatedDate;

        vehicle.Update(UpdatedRegistrationNumber, UpdatedMake, UpdatedModel, UpdatedYear, UpdatedDailyRate, timeProvider);

        Assert.Equal(UpdatedRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(UpdatedMake, vehicle.Make);
        Assert.Equal(UpdatedModel, vehicle.Model);
        Assert.Equal(UpdatedYear, vehicle.Year);
        Assert.Equal(UpdatedDailyRate, vehicle.DailyRate);
        Assert.Equal(originalId, vehicle.Id);
        Assert.Equal(originalCreatedDate, vehicle.CreatedDate);
        Assert.False(vehicle.IsDeleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankRegistrationNumber_ThrowsDomainRuleViolationException_AndLeavesVehicleUnchanged(string blank)
    {
        var vehicle = CreateValidVehicle();

        var act = () => vehicle.Update(blank, UpdatedMake, UpdatedModel, UpdatedYear, UpdatedDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankMake_ThrowsDomainRuleViolationException_AndLeavesVehicleUnchanged(string blank)
    {
        var vehicle = CreateValidVehicle();

        var act = () => vehicle.Update(UpdatedRegistrationNumber, blank, UpdatedModel, UpdatedYear, UpdatedDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankModel_ThrowsDomainRuleViolationException_AndLeavesVehicleUnchanged(string blank)
    {
        var vehicle = CreateValidVehicle();

        var act = () => vehicle.Update(UpdatedRegistrationNumber, UpdatedMake, blank, UpdatedYear, UpdatedDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-350.50)]
    public void Update_WithNonPositiveDailyRate_ThrowsDomainRuleViolationException_AndLeavesVehicleUnchanged(decimal nonPositiveRate)
    {
        var vehicle = CreateValidVehicle();

        var act = () => vehicle.Update(UpdatedRegistrationNumber, UpdatedMake, UpdatedModel, UpdatedYear, nonPositiveRate);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Fact]
    public void Update_WithYearBefore1900_ThrowsDomainRuleViolationException_AndLeavesVehicleUnchanged()
    {
        var vehicle = CreateValidVehicle();

        var act = () => vehicle.Update(UpdatedRegistrationNumber, UpdatedMake, UpdatedModel, 1899, UpdatedDailyRate);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Fact]
    public void Update_WithYearTwoYearsFromNow_ThrowsDomainRuleViolationException_AndLeavesVehicleUnchanged()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var vehicle = CreateValidVehicle(timeProvider);

        var act = () => vehicle.Update(
            UpdatedRegistrationNumber, UpdatedMake, UpdatedModel, fixedNow.Year + 2, UpdatedDailyRate, timeProvider);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidRegistrationNumber, vehicle.RegistrationNumber);
        Assert.Equal(ValidMake, vehicle.Make);
        Assert.Equal(ValidModel, vehicle.Model);
        Assert.Equal(ValidYear, vehicle.Year);
        Assert.Equal(ValidDailyRate, vehicle.DailyRate);
    }

    [Fact]
    public void Update_WithYearEqualToNextCalendarYear_Succeeds()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var vehicle = CreateValidVehicle(timeProvider);

        vehicle.Update(
            UpdatedRegistrationNumber, UpdatedMake, UpdatedModel, fixedNow.Year + 1, UpdatedDailyRate, timeProvider);

        Assert.Equal(fixedNow.Year + 1, vehicle.Year);
    }

    [Fact]
    public void SoftDelete_MarksVehicleAsDeleted()
    {
        var vehicle = CreateValidVehicle();

        vehicle.SoftDelete();

        Assert.True(vehicle.IsDeleted);
    }

    [Fact]
    public void SoftDelete_CalledTwice_StaysDeletedAndDoesNotThrow()
    {
        var vehicle = CreateValidVehicle();

        vehicle.SoftDelete();
        var act = () => vehicle.SoftDelete();

        var exception = Record.Exception(act);
        Assert.Null(exception);
        Assert.True(vehicle.IsDeleted);
    }

    [Fact]
    public void Vehicle_HasNoPublicSetters()
    {
        var propertiesWithPublicSetters = typeof(Vehicle)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }
}
