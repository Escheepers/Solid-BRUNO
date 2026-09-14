using BrunoVehicleHire.Application.Customers.Commands;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Boundary-case coverage for <see cref="CreateCustomerCommandValidator"/>: FirstName/LastName/
/// PhoneNumber must not be blank, Email must not be blank AND must be a well-formed address
/// (unlike Vehicle's Year, email FORMAT deliberately IS a shape check here -- see spec-3-1's
/// Boundaries -- so a malformed email fails validation at this 400 layer, not as a domain 409).
/// </summary>
public class CreateCustomerCommandValidatorTests
{
    private readonly CreateCustomerCommandValidator _validator = new();

    private static CreateCustomerCommand ValidCommand(
        string firstName = "Jane",
        string lastName = "Doe",
        string email = "jane.doe@example.com",
        string phoneNumber = "0821234567") => new(firstName, lastName, email, phoneNumber);

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
    [InlineData("")]
    [InlineData("   ")]
    public void Email_Blank_FailsValidation(string email)
    {
        var result = _validator.TestValidate(ValidCommand(email: email));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Email_Malformed_FailsValidation()
    {
        var result = _validator.TestValidate(ValidCommand(email: "not-an-email"));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Email_WellFormed_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.Email);
    }
}
