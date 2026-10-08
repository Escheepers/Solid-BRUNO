using BrunoVehicleHire.Application.Customers.Commands;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Boundary-case coverage for <see cref="UpdateCustomerCommandValidator"/> -- identical field rules
/// to <see cref="CreateCustomerCommandValidatorTests"/>: FirstName/LastName/PhoneNumber must not be
/// blank, Email must not be blank and must be well-formed. Mirrors
/// <c>UpdateVehicleCommandValidatorTests</c>'s exact structure.
/// </summary>
public class UpdateCustomerCommandValidatorTests
{
    private readonly UpdateCustomerCommandValidator _validator = new();

    private static UpdateCustomerCommand ValidCommand(
        Guid? customerId = null,
        string firstName = "Jane",
        string lastName = "Doe",
        string email = "jane.doe@example.com",
        string phoneNumber = "0821234567") =>
        new(customerId ?? Guid.NewGuid(), firstName, lastName, email, phoneNumber);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FirstName_Blank_FailsValidation(string firstName)
    {
        var result = _validator.TestValidate(ValidCommand(firstName: firstName));

        result.ShouldHaveValidationErrorFor(c => c.FirstName);
    }

    [Fact]
    public void FirstName_NonBlank_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.FirstName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LastName_Blank_FailsValidation(string lastName)
    {
        var result = _validator.TestValidate(ValidCommand(lastName: lastName));

        result.ShouldHaveValidationErrorFor(c => c.LastName);
    }

    [Fact]
    public void LastName_NonBlank_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.LastName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PhoneNumber_Blank_FailsValidation(string phoneNumber)
    {
        var result = _validator.TestValidate(ValidCommand(phoneNumber: phoneNumber));

        result.ShouldHaveValidationErrorFor(c => c.PhoneNumber);
    }

    [Fact]
    public void PhoneNumber_NonBlank_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.PhoneNumber);
    }

    [Theory]
    [InlineData("08212345a7")]
    [InlineData("082-123-4567")]
    [InlineData("082 123 4567")]
    [InlineData("+27821234567")]
    [InlineData("08212345678")]
    public void PhoneNumber_NotDigitsOnlyOrLongerThanTenDigits_FailsValidation(string phoneNumber)
    {
        var result = _validator.TestValidate(ValidCommand(phoneNumber: phoneNumber));

        result.ShouldHaveValidationErrorFor(c => c.PhoneNumber);
    }

    [Theory]
    [InlineData("0821234567")]
    [InlineData("12345")]
    public void PhoneNumber_OneToTenDigitsOnly_PassesValidation(string phoneNumber)
    {
        var result = _validator.TestValidate(ValidCommand(phoneNumber: phoneNumber));

        result.ShouldNotHaveValidationErrorFor(c => c.PhoneNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void Email_BlankOrMalformed_FailsValidation(string email)
    {
        var result = _validator.TestValidate(ValidCommand(email: email));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Email_WellFormed_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.Email);
    }
}
