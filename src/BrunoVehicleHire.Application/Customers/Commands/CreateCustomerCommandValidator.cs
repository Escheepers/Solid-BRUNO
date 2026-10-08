using FluentValidation;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="CreateCustomerCommand"/>: FirstName/LastName/PhoneNumber
/// must not be blank, Email must not be blank AND must be a well-formed address. Unlike Vehicle's
/// Year (a domain-only 409 concern), email FORMAT deliberately IS validated here (a 400-level shape
/// check) -- <c>Customer.Create()</c> itself never checks format, only blankness (see spec-3-1's
/// Boundaries). Run through the shared <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR
/// pipeline -- never invoked manually by <see cref="CreateCustomerCommandHandler"/>.
/// </summary>
public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(c => c.FirstName)
            .NotEmpty();

        RuleFor(c => c.LastName)
            .NotEmpty();

        RuleFor(c => c.PhoneNumber)
            .NotEmpty()
            .Matches(PhoneNumberRules.DigitsOnlyUpToTenPattern)
            .WithMessage(PhoneNumberRules.Message);

        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress();
    }
}
