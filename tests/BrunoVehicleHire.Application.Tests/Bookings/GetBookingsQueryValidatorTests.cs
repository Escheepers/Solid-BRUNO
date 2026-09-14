using BrunoVehicleHire.Application.Bookings.Queries;
using FluentValidation.TestHelper;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Boundary-case coverage for <see cref="GetBookingsQueryValidator"/>: <c>Page</c> must be at least
/// 1, <c>PageSize</c> must fall within [1, 100] inclusive. Mirrors
/// <c>GetVehiclesQueryValidatorTests</c>'s exact coverage.
/// </summary>
public class GetBookingsQueryValidatorTests
{
    private readonly GetBookingsQueryValidator _validator = new();

    [Fact]
    public void Page_Zero_FailsValidation()
    {
        var result = _validator.TestValidate(new GetBookingsQuery(Page: 0, PageSize: 20));

        result.ShouldHaveValidationErrorFor(q => q.Page);
    }

    [Fact]
    public void Page_One_PassesValidation()
    {
        var result = _validator.TestValidate(new GetBookingsQuery(Page: 1, PageSize: 20));

        result.ShouldNotHaveValidationErrorFor(q => q.Page);
    }

    [Fact]
    public void PageSize_Zero_FailsValidation()
    {
        var result = _validator.TestValidate(new GetBookingsQuery(Page: 1, PageSize: 0));

        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_OneHundredAndOne_FailsValidation()
    {
        var result = _validator.TestValidate(new GetBookingsQuery(Page: 1, PageSize: 101));

        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_OneHundred_PassesValidation()
    {
        var result = _validator.TestValidate(new GetBookingsQuery(Page: 1, PageSize: 100));

        result.ShouldNotHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_One_PassesValidation()
    {
        var result = _validator.TestValidate(new GetBookingsQuery(Page: 1, PageSize: 1));

        result.ShouldNotHaveValidationErrorFor(q => q.PageSize);
    }
}
