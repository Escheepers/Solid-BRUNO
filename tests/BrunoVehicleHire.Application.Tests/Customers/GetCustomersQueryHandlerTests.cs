using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Queries;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Proves <see cref="GetCustomersQueryHandler"/> passes paging/search parameters straight through
/// to <see cref="ICustomerRepository"/> (mocked via NSubstitute) and correctly maps each domain
/// <see cref="Customer"/> to a <c>CustomerDto</c>, wrapped in the shared <c>PagedResult&lt;T&gt;</c>
/// shape. Mirrors <c>GetVehiclesQueryHandlerTests</c>'s exact coverage.
/// </summary>
public class GetCustomersQueryHandlerTests
{
    [Fact]
    public async Task Handle_MultiItemPage_MapsEachCustomerAndWrapsInPagedResult()
    {
        var customerOne = Customer.Create("Jane", "Doe", "jane@example.com", "0821111111");
        var customerTwo = Customer.Create("John", "Smith", "john@example.com", "0822222222");

        var repository = Substitute.For<ICustomerRepository>();
        repository.GetPagedAsync(2, 10, "Doe", false, Arg.Any<CancellationToken>())
            .Returns((new List<Customer> { customerOne, customerTwo }, 27));

        var handler = new GetCustomersQueryHandler(repository);

        var result = await handler.Handle(new GetCustomersQuery(2, 10, "Doe"), CancellationToken.None);

        result.TotalCount.Should().Be(27);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.Items.Should().HaveCount(2);

        result.Items[0].Id.Should().Be(customerOne.Id);
        result.Items[0].FirstName.Should().Be(customerOne.FirstName);
        result.Items[0].LastName.Should().Be(customerOne.LastName);
        result.Items[0].Email.Should().Be(customerOne.Email);
        result.Items[0].PhoneNumber.Should().Be(customerOne.PhoneNumber);
        result.Items[0].CreatedDate.Should().Be(customerOne.CreatedDate);

        result.Items[1].Id.Should().Be(customerTwo.Id);

        await repository.Received(1).GetPagedAsync(2, 10, "Doe", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoResults_ReturnsEmptyItemsWithZeroTotalCount()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetPagedAsync(1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns((new List<Customer>(), 0));

        var handler = new GetCustomersQueryHandler(repository);

        var result = await handler.Handle(new GetCustomersQuery(1, 20, null), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_IncludeInactiveTrue_PassesThroughToRepository()
    {
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetPagedAsync(1, 20, null, true, Arg.Any<CancellationToken>())
            .Returns((new List<Customer>(), 0));

        var handler = new GetCustomersQueryHandler(repository);

        await handler.Handle(new GetCustomersQuery(1, 20, null, IncludeInactive: true), CancellationToken.None);

        await repository.Received(1).GetPagedAsync(1, 20, null, true, Arg.Any<CancellationToken>());
    }
}
