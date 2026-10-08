using BrunoVehicleHire.Application.Vehicles.Commands;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Boundary-case coverage for <see cref="CreateVehicleCommandValidator"/>: RegistrationNumber/Make/
/// Model must not be blank, DailyRate must be strictly positive. <c>Year</c> is deliberately never
/// asserted here -- its plausible-range check stays a pure <c>Vehicle.Create()</c> domain invariant
/// (409), never duplicated in this validator (see spec-2-1's Design Notes).
/// </summary>
public class CreateVehicleCommandValidatorTests
{
    private readonly CreateVehicleCommandValidator _validator = new();

    private static CreateVehicleCommand ValidCommand(
        string registrationNumber = "CA123456",
        string make = "Toyota",
        string model = "Corolla",
        int year = 2023,
        decimal dailyRate = 350m) => new(registrationNumber, make, model, year, dailyRate);

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

    [Theory]
    [InlineData(1000000.01)]
    [InlineData(99999999999999.99)]
    public void DailyRate_AboveTheMaximum_FailsValidation(decimal dailyRate)
    {
        var result = _validator.TestValidate(ValidCommand(dailyRate: dailyRate));

        result.ShouldHaveValidationErrorFor(c => c.DailyRate);
    }

    [Theory]
    [InlineData(350.555)]
    [InlineData(0.001)]
    public void DailyRate_WithMoreThanTwoDecimals_FailsValidation(decimal dailyRate)
    {
        var result = _validator.TestValidate(ValidCommand(dailyRate: dailyRate));

        result.ShouldHaveValidationErrorFor(c => c.DailyRate);
    }

    [Theory]
    [InlineData(350)]
    [InlineData(350.5)]
    [InlineData(1000000)]
    public void DailyRate_WithinTheMaximumAndWholeCents_PassesValidation(decimal dailyRate)
    {
        var result = _validator.TestValidate(ValidCommand(dailyRate: dailyRate));

        result.ShouldNotHaveValidationErrorFor(c => c.DailyRate);
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
