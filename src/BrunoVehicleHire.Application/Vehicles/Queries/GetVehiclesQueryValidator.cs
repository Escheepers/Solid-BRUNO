using FluentValidation;

namespace BrunoVehicleHire.Application.Vehicles.Queries;

/// <summary>
/// Boundary rules for <see cref="GetVehiclesQuery"/>: <c>Page</c> must be at least 1, <c>PageSize</c>
/// must fall within [1, 100] inclusive. Run through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never invoked manually
/// by <see cref="GetVehiclesQueryHandler"/>.
/// </summary>
public class GetVehiclesQueryValidator : AbstractValidator<GetVehiclesQuery>
{
    public GetVehiclesQueryValidator()
    {
        RuleFor(q => q.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, 100);
    }
}
