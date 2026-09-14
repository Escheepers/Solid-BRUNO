using FluentValidation;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Pure input-shape rules for <see cref="CreateBookingCommand"/>: <c>EndDate</c> must be strictly
/// after <c>StartDate</c>. This deliberately duplicates <c>Booking.Create</c>'s already-shipped
/// (Story 3.3) domain invariant of the same name -- spec-4-1's Scope decision 1: the AC for this
/// story requires a plain <c>400</c> for this specific error via this HTTP path, and FluentValidation
/// runs before the handler (and therefore before <c>Booking.Create</c>) in the
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> pipeline, so its 400 always wins the race. The
/// domain check stays completely unmodified as defense-in-depth for any other caller of
/// <c>Booking.Create</c> -- this validator is boundary-level duplication, not a replacement. Run
/// through the shared pipeline -- never invoked manually by <see cref="CreateBookingCommandHandler"/>.
/// </summary>
public class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(c => c.EndDate)
            .GreaterThan(c => c.StartDate)
            .WithMessage("EndDate must be after StartDate.");
    }
}
