using BrunoVehicleHire.Application.Vehicles.Queries;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Boundary-case coverage for <see cref="GetVehiclesQueryValidator"/>: <c>Page</c> must be at least
/// 1, <c>PageSize</c> must fall within [1, 100] inclusive. Run through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> pipeline in production -- never invoked
/// manually by the handler.
/// </summary>
public class GetVehiclesQueryValidatorTests
{
    private readonly GetVehiclesQueryValidator _validator = new();

    [Fact]
    public void Page_Zero_FailsValidation()
    {
        var result = _validator.TestValidate(new GetVehiclesQuery(Page: 0, PageSize: 20, Search: null));

        result.ShouldHaveValidationErrorFor(q => q.Page);
    }

    [Fact]
    public void Page_One_PassesValidation()
    {
        var result = _validator.TestValidate(new GetVehiclesQuery(Page: 1, PageSize: 20, Search: null));

        result.ShouldNotHaveValidationErrorFor(q => q.Page);
    }

    [Fact]
    public void PageSize_Zero_FailsValidation()
    {
        var result = _validator.TestValidate(new GetVehiclesQuery(Page: 1, PageSize: 0, Search: null));

        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_OneHundredAndOne_FailsValidation()
    {
        var result = _validator.TestValidate(new GetVehiclesQuery(Page: 1, PageSize: 101, Search: null));

        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_OneHundred_PassesValidation()
    {
        var result = _validator.TestValidate(new GetVehiclesQuery(Page: 1, PageSize: 100, Search: null));

        result.ShouldNotHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_One_PassesValidation()
    {
        var result = _validator.TestValidate(new GetVehiclesQuery(Page: 1, PageSize: 1, Search: null));

        result.ShouldNotHaveValidationErrorFor(q => q.PageSize);
    }
}
