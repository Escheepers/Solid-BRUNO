using FluentValidation;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="CreateVehicleCommand"/>: RegistrationNumber/Make/Model must
/// not be blank, DailyRate must be strictly positive. <c>Year</c>'s plausible-range check is
/// deliberately NOT duplicated here -- it stays a pure <c>Vehicle.Create()</c> domain invariant
/// (surfaced as a 409 by <c>GlobalExceptionHandler</c>), since its bound depends on the current date
/// rather than being a context-free shape check (see spec-2-1's Design Notes). Run through the
/// shared <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never invoked
/// manually by <see cref="CreateVehicleCommandHandler"/>.
/// </summary>
public class CreateVehicleCommandValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleCommandValidator()
    {
        RuleFor(c => c.RegistrationNumber)
            .NotEmpty();

        RuleFor(c => c.Make)
            .NotEmpty();

        RuleFor(c => c.Model)
            .NotEmpty();

        RuleFor(c => c.DailyRate)
            .MustBeAValidDailyRate();
    }
}
