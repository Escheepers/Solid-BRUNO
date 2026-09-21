using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Queries;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Proves <see cref="GetBookingsQueryHandler"/> passes paging parameters straight through to
/// <see cref="IBookingRepository"/> (mocked via NSubstitute) and correctly maps each
/// <c>(Booking, Vehicle, Customer)</c> tuple to a <c>BookingDto</c>, wrapped in the shared
/// <c>PagedResult&lt;T&gt;</c> shape -- never validated here (that's
/// <see cref="GetBookingsQueryValidator"/>'s job via the pipeline). Includes a row whose Customer is
/// anonymized, proving <c>IsAnonymized</c> flows through to the DTO unchanged (spec-4-1's I/O matrix:
/// the list must still render such a row, never drop it).
/// </summary>
public class GetBookingsQueryHandlerTests
{
    private static Vehicle SomeVehicle() => Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    private static Customer SomeCustomer() => Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    [Fact]
    public async Task Handle_MultiItemPage_MapsEachTupleAndWrapsInPagedResult()
    {
        var vehicleOne = SomeVehicle();
        var customerOne = SomeCustomer();
        var bookingOne = Booking.Create(
            vehicleOne.Id, customerOne.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var vehicleTwo = SomeVehicle();
        var customerTwo = SomeCustomer();
        customerTwo.Anonymize();
        var bookingTwo = Booking.Create(
            vehicleTwo.Id, customerTwo.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), 700m);

        var repository = Substitute.For<IBookingRepository>();
        repository.GetPagedAsync(2, 10, null, null, Arg.Any<CancellationToken>())
            .Returns((
                new List<(Booking, Vehicle, Customer)>
                {
                    (bookingOne, vehicleOne, customerOne),
                    (bookingTwo, vehicleTwo, customerTwo),
                },
                27));

        var handler = new GetBookingsQueryHandler(repository);

        var result = await handler.Handle(new GetBookingsQuery(2, 10), CancellationToken.None);

        result.TotalCount.Should().Be(27);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.Items.Should().HaveCount(2);

        result.Items[0].Id.Should().Be(bookingOne.Id);
        result.Items[0].VehicleMake.Should().Be(vehicleOne.Make);
        result.Items[0].CustomerFirstName.Should().Be(customerOne.FirstName);
        result.Items[0].CustomerIsAnonymized.Should().BeFalse();

        result.Items[1].Id.Should().Be(bookingTwo.Id);
        result.Items[1].CustomerIsAnonymized.Should().BeTrue();

        await repository.Received(1).GetPagedAsync(2, 10, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoResults_ReturnsEmptyItemsWithZeroTotalCount()
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.GetPagedAsync(1, 20, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<(Booking, Vehicle, Customer)>(), 0));

        var handler = new GetBookingsQueryHandler(repository);

        var result = await handler.Handle(new GetBookingsQuery(1, 20), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    /// <summary>
    /// Proves the optional <c>VehicleId</c> filter (spec-4-5's Scope decision 1) is passed straight
    /// through to <see cref="IBookingRepository.GetPagedAsync"/> unchanged -- the handler applies no
    /// filtering logic of its own.
    /// </summary>
    [Fact]
    public async Task Handle_VehicleIdSupplied_PassesItStraightThroughToRepository()
    {
        var vehicle = SomeVehicle();
        var customer = SomeCustomer();
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var repository = Substitute.For<IBookingRepository>();
        repository.GetPagedAsync(1, 20, vehicle.Id, null, Arg.Any<CancellationToken>())
            .Returns((new List<(Booking, Vehicle, Customer)> { (booking, vehicle, customer) }, 1));

        var handler = new GetBookingsQueryHandler(repository);

        var result = await handler.Handle(new GetBookingsQuery(1, 20, vehicle.Id), CancellationToken.None);

        result.Items.Should().ContainSingle(b => b.Id == booking.Id);

        await repository.Received(1).GetPagedAsync(1, 20, vehicle.Id, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleIdOmitted_DefaultsToNullFilter()
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.GetPagedAsync(1, 20, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<(Booking, Vehicle, Customer)>(), 0));

        var handler = new GetBookingsQueryHandler(repository);

        await handler.Handle(new GetBookingsQuery(1, 20), CancellationToken.None);

        await repository.Received(1).GetPagedAsync(1, 20, null, null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves the optional <c>Search</c> filter (spec-bookings-search) is passed straight through to
    /// <see cref="IBookingRepository.GetPagedAsync"/> unchanged -- the handler applies no filtering
    /// logic of its own, mirroring <see cref="Handle_VehicleIdSupplied_PassesItStraightThroughToRepository"/>'s
    /// exact reasoning for the sibling optional filter.
    /// </summary>
    [Fact]
    public async Task Handle_SearchSupplied_PassesItStraightThroughToRepository()
    {
        var vehicle = SomeVehicle();
        var customer = SomeCustomer();
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var repository = Substitute.For<IBookingRepository>();
        repository.GetPagedAsync(1, 20, null, "Ferrari", Arg.Any<CancellationToken>())
            .Returns((new List<(Booking, Vehicle, Customer)> { (booking, vehicle, customer) }, 1));

        var handler = new GetBookingsQueryHandler(repository);

        var result = await handler.Handle(new GetBookingsQuery(1, 20, Search: "Ferrari"), CancellationToken.None);

        result.Items.Should().ContainSingle(b => b.Id == booking.Id);

        await repository.Received(1).GetPagedAsync(1, 20, null, "Ferrari", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SearchOmitted_DefaultsToNullFilter()
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.GetPagedAsync(1, 20, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<(Booking, Vehicle, Customer)>(), 0));

        var handler = new GetBookingsQueryHandler(repository);

        await handler.Handle(new GetBookingsQuery(1, 20), CancellationToken.None);

        await repository.Received(1).GetPagedAsync(1, 20, null, null, Arg.Any<CancellationToken>());
    }
}
