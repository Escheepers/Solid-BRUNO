using FluentValidation;

namespace BrunoVehicleHire.Application.Bookings.Queries;

/// <summary>
/// Boundary rules for <see cref="GetBookingsQuery"/>: <c>Page</c> must be at least 1, <c>PageSize</c>
/// must fall within [1, 100] inclusive. Mirrors <c>GetVehiclesQueryValidator</c>/
/// <c>GetCustomersQueryValidator</c>'s exact rules. Run through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never invoked manually
/// by <see cref="GetBookingsQueryHandler"/>.
/// </summary>
public class GetBookingsQueryValidator : AbstractValidator<GetBookingsQuery>
{
    public GetBookingsQueryValidator()
    {
        RuleFor(q => q.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, 100);
    }
}
