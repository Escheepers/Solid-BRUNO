using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Queries;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="GetVehiclesQueryHandler"/> passes paging/search parameters straight through to
/// <see cref="IVehicleRepository"/> (mocked via NSubstitute) and correctly maps each domain
/// <see cref="Vehicle"/> to a <c>VehicleDto</c>, wrapped in the shared <c>PagedResult&lt;T&gt;</c>
/// shape -- never validated here (that's <see cref="GetVehiclesQueryValidator"/>'s job via the
/// pipeline).
/// </summary>
public class GetVehiclesQueryHandlerTests
{
    [Fact]
    public async Task Handle_MultiItemPage_MapsEachVehicleAndWrapsInPagedResult()
    {
        var vehicleOne = Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);
        var vehicleTwo = Vehicle.Create("CA654321", "Honda", "Civic", 2024, 400m);

        var repository = Substitute.For<IVehicleRepository>();
        repository.GetPagedAsync(2, 10, "Toyota", false, Arg.Any<CancellationToken>())
            .Returns((new List<Vehicle> { vehicleOne, vehicleTwo }, 27));

        var handler = new GetVehiclesQueryHandler(repository);

        var result = await handler.Handle(new GetVehiclesQuery(2, 10, "Toyota"), CancellationToken.None);

        result.TotalCount.Should().Be(27);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.Items.Should().HaveCount(2);

        result.Items[0].Id.Should().Be(vehicleOne.Id);
        result.Items[0].RegistrationNumber.Should().Be(vehicleOne.RegistrationNumber);
        result.Items[0].Make.Should().Be(vehicleOne.Make);
        result.Items[0].Model.Should().Be(vehicleOne.Model);
        result.Items[0].Year.Should().Be(vehicleOne.Year);
        result.Items[0].DailyRate.Should().Be(vehicleOne.DailyRate);
        result.Items[0].CreatedDate.Should().Be(vehicleOne.CreatedDate);

        result.Items[1].Id.Should().Be(vehicleTwo.Id);
        result.Items[1].RegistrationNumber.Should().Be(vehicleTwo.RegistrationNumber);

        await repository.Received(1).GetPagedAsync(2, 10, "Toyota", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoResults_ReturnsEmptyItemsWithZeroTotalCount()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.GetPagedAsync(1, 20, null, false, Arg.Any<CancellationToken>())
            .Returns((new List<Vehicle>(), 0));

        var handler = new GetVehiclesQueryHandler(repository);

        var result = await handler.Handle(new GetVehiclesQuery(1, 20, null), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_IncludeInactiveTrue_PassesThroughToRepository()
    {
        var repository = Substitute.For<IVehicleRepository>();
        repository.GetPagedAsync(1, 20, null, true, Arg.Any<CancellationToken>())
            .Returns((new List<Vehicle>(), 0));

        var handler = new GetVehiclesQueryHandler(repository);

        await handler.Handle(new GetVehiclesQuery(1, 20, null, IncludeInactive: true), CancellationToken.None);

        await repository.Received(1).GetPagedAsync(1, 20, null, true, Arg.Any<CancellationToken>());
    }
}
