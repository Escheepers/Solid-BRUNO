using FluentValidation;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="UpdateCustomerCommand"/>: identical shape to
/// <see cref="CreateCustomerCommandValidator"/> -- FirstName/LastName/PhoneNumber must not be blank,
/// Email must not be blank AND must be a well-formed address. Mirrors
/// <see cref="UpdateVehicleCommandValidator"/>'s exact reasoning: this is a 400-level shape check,
/// entirely distinct from <c>Customer.Update()</c>'s own domain-invariant (blankness-only)
/// enforcement. Run through the shared <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR
/// pipeline -- never invoked manually by <see cref="UpdateCustomerCommandHandler"/>.
/// </summary>
public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(c => c.FirstName)
            .NotEmpty();

        RuleFor(c => c.LastName)
            .NotEmpty();

        RuleFor(c => c.PhoneNumber)
            .NotEmpty();

        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress();
    }
}
