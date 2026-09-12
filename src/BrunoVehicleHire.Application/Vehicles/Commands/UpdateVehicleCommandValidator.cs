using FluentValidation;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="UpdateVehicleCommand"/>: RegistrationNumber/Make/Model must
/// not be blank, DailyRate must be strictly positive -- identical shape to
/// <see cref="CreateVehicleCommandValidator"/>. <c>Year</c>'s plausible-range check is deliberately
/// NOT duplicated here -- it stays a pure <c>Vehicle.Update()</c> domain invariant (surfaced as a
/// 409 by <c>GlobalExceptionHandler</c>), same reasoning as Create (see spec-2-1's Design Notes). Run
/// through the shared <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never
/// invoked manually by <see cref="UpdateVehicleCommandHandler"/>.
/// </summary>
public class UpdateVehicleCommandValidator : AbstractValidator<UpdateVehicleCommand>
{
    public UpdateVehicleCommandValidator()
    {
        RuleFor(c => c.RegistrationNumber)
            .NotEmpty();

        RuleFor(c => c.Make)
            .NotEmpty();

        RuleFor(c => c.Model)
            .NotEmpty();

        RuleFor(c => c.DailyRate)
            .GreaterThan(0);
    }
}
