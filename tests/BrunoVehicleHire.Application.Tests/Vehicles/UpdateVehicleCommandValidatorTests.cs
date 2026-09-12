using BrunoVehicleHire.Application.Vehicles.Commands;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Boundary-case coverage for <see cref="UpdateVehicleCommandValidator"/> -- identical field rules
/// to <see cref="CreateVehicleCommandValidatorTests"/>: RegistrationNumber/Make/Model must not be
/// blank, DailyRate must be strictly positive. <c>Year</c> is deliberately never asserted here for
/// the same reason as Create's validator: its plausible-range check stays a pure
/// <c>Vehicle.Update()</c> domain invariant (409), not a shape check (see spec-2-1's Design Notes,
/// reused unchanged by spec-2-2).
/// </summary>
public class UpdateVehicleCommandValidatorTests
{
    private readonly UpdateVehicleCommandValidator _validator = new();

    private static UpdateVehicleCommand ValidCommand(
        Guid? vehicleId = null,
        string registrationNumber = "CA123456",
        string make = "Toyota",
        string model = "Corolla",
        int year = 2023,
        decimal dailyRate = 350m) =>
        new(vehicleId ?? Guid.NewGuid(), registrationNumber, make, model, year, dailyRate);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RegistrationNumber_Blank_FailsValidation(string registrationNumber)
    {
        var result = _validator.TestValidate(ValidCommand(registrationNumber: registrationNumber));

        result.ShouldHaveValidationErrorFor(c => c.RegistrationNumber);
    }

    [Fact]
    public void RegistrationNumber_NonBlank_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.RegistrationNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Make_Blank_FailsValidation(string make)
    {
        var result = _validator.TestValidate(ValidCommand(make: make));

        result.ShouldHaveValidationErrorFor(c => c.Make);
    }

    [Fact]
    public void Make_NonBlank_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.Make);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Model_Blank_FailsValidation(string model)
    {
        var result = _validator.TestValidate(ValidCommand(model: model));

        result.ShouldHaveValidationErrorFor(c => c.Model);
    }

    [Fact]
    public void Model_NonBlank_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.Model);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DailyRate_NotPositive_FailsValidation(decimal dailyRate)
    {
        var result = _validator.TestValidate(ValidCommand(dailyRate: dailyRate));

        result.ShouldHaveValidationErrorFor(c => c.DailyRate);
    }

    [Fact]
    public void DailyRate_Positive_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.DailyRate);
    }

    [Fact]
    public void Year_Implausible_StillPassesValidation_BecauseYearIsADomainConcernNotAShapeCheck()
    {
        var result = _validator.TestValidate(ValidCommand(year: 3000));

        result.ShouldNotHaveValidationErrorFor(c => c.Year);
    }
}
