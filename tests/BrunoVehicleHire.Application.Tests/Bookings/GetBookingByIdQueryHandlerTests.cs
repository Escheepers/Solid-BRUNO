using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Queries;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Proves <see cref="GetBookingByIdQueryHandler"/>'s orchestration:
/// <c>GetByIdWithVehicleAndCustomerAsync</c> (mocked <see cref="IBookingRepository"/>) -> not-found
/// check -> <c>BookingDto.FromDomain</c>. Written before the handler existed (TDD) -- mirrors
/// <c>GetVehicleByIdQueryHandlerTests</c>'s exact shape. Includes a case whose Customer is
/// anonymized, proving the detail query resolves it exactly like the list query does (spec-4-5's
/// Scope decision 2/AC).
/// </summary>
public class GetBookingByIdQueryHandlerTests
{
    private static Vehicle SomeVehicle() => Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    private static Customer SomeCustomer() => Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    [Fact]
    public async Task Handle_BookingExists_ReturnsCorrectlyMappedDto()
    {
        var vehicle = SomeVehicle();
        var customer = SomeCustomer();
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var repository = Substitute.For<IBookingRepository>();
        repository.GetByIdWithVehicleAndCustomerAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns((booking, vehicle, customer));

        var handler = new GetBookingByIdQueryHandler(repository);

        var result = await handler.Handle(new GetBookingByIdQuery(booking.Id), CancellationToken.None);

        result.Id.Should().Be(booking.Id);
        result.VehicleMake.Should().Be(vehicle.Make);
        result.VehicleModel.Should().Be(vehicle.Model);
        result.VehicleRegistrationNumber.Should().Be(vehicle.RegistrationNumber);
        result.CustomerFirstName.Should().Be(customer.FirstName);
        result.CustomerLastName.Should().Be(customer.LastName);
        result.CustomerIsAnonymized.Should().BeFalse();
        result.StartDate.Should().Be(booking.StartDate);
        result.EndDate.Should().Be(booking.EndDate);
        result.TotalPrice.Should().Be(booking.TotalPrice);
        result.Status.Should().Be(booking.Status);
    }

    [Fact]
    public async Task Handle_BookingsCustomerIsAnonymized_ReturnsDtoWithCustomerIsAnonymizedTrue()
    {
        var vehicle = SomeVehicle();
        var customer = SomeCustomer();
        customer.Anonymize();
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var repository = Substitute.For<IBookingRepository>();
        repository.GetByIdWithVehicleAndCustomerAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns((booking, vehicle, customer));

        var handler = new GetBookingByIdQueryHandler(repository);

        var result = await handler.Handle(new GetBookingByIdQuery(booking.Id), CancellationToken.None);

        result.CustomerIsAnonymized.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_BookingDoesNotExistAtAll_ThrowsNotFoundException()
    {
        var repository = Substitute.For<IBookingRepository>();
        var missingId = Guid.NewGuid();

        repository.GetByIdWithVehicleAndCustomerAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(((Booking, Vehicle, Customer)?)null);

        var handler = new GetBookingByIdQueryHandler(repository);

        var act = async () => await handler.Handle(new GetBookingByIdQuery(missingId), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Booking));
        exception.Which.Id.Should().Be(missingId);
    }
}
