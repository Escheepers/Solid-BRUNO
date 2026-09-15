using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.CustomerSummaries.Queries;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.CustomerSummaries;

/// <summary>
/// Proves <see cref="GetCustomerSummaryQueryHandler"/>'s orchestration:
/// <c>GetByIdIncludingSoftDeletedAsync</c> (mocked <see cref="ICustomerRepository"/>) -> not-found
/// check -> <c>GetForCustomerWithVehicleAsync</c> (mocked <see cref="IBookingRepository"/>) ->
/// <c>CustomerSummaryDto.FromDomain</c>. Written before the handler existed (TDD) -- mirrors
/// <c>GetBookingByIdQueryHandlerTests</c>'s exact shape. Covers every row of spec-5-1's own I/O &amp;
/// Edge-Case Matrix that is a backend concern.
/// </summary>
public class GetCustomerSummaryQueryHandlerTests
{
    private static Vehicle SomeVehicle(string registrationNumber = "CA123456") =>
        Vehicle.Create(registrationNumber, "Toyota", "Corolla", 2023, 350m);

    private static Customer SomeCustomer() => Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    private static (ICustomerRepository Customers, IBookingRepository Bookings) MockRepositories() =>
        (Substitute.For<ICustomerRepository>(), Substitute.For<IBookingRepository>());

    [Fact]
    public async Task Handle_CustomerWithBookingHistory_ReturnsIdentityContactAndFullBookingList()
    {
        var customer = SomeCustomer();
        var vehicle = SomeVehicle();
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var (customerRepository, bookingRepository) = MockRepositories();
        customerRepository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);
        bookingRepository.GetForCustomerWithVehicleAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<(Booking, Vehicle)> { (booking, vehicle) });

        var handler = new GetCustomerSummaryQueryHandler(customerRepository, bookingRepository);

        var result = await handler.Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        result.Id.Should().Be(customer.Id);
        result.FirstName.Should().Be(customer.FirstName);
        result.LastName.Should().Be(customer.LastName);
        result.Email.Should().Be(customer.Email);
        result.PhoneNumber.Should().Be(customer.PhoneNumber);
        result.IsAnonymized.Should().BeFalse();
        result.Bookings.Should().HaveCount(1);

        var bookingDto = result.Bookings[0];
        bookingDto.Id.Should().Be(booking.Id);
        bookingDto.VehicleMake.Should().Be(vehicle.Make);
        bookingDto.VehicleModel.Should().Be(vehicle.Model);
        bookingDto.VehicleRegistrationNumber.Should().Be(vehicle.RegistrationNumber);
        bookingDto.StartDate.Should().Be(booking.StartDate);
        bookingDto.EndDate.Should().Be(booking.EndDate);
        bookingDto.TotalPrice.Should().Be(booking.TotalPrice);
        bookingDto.Status.Should().Be(booking.Status);
    }

    [Fact]
    public async Task Handle_CustomerWithZeroBookings_ReturnsIdentityContactWithEmptyBookingList()
    {
        var customer = SomeCustomer();

        var (customerRepository, bookingRepository) = MockRepositories();
        customerRepository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);
        bookingRepository.GetForCustomerWithVehicleAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<(Booking, Vehicle)>());

        var handler = new GetCustomerSummaryQueryHandler(customerRepository, bookingRepository);

        var result = await handler.Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        result.FirstName.Should().Be(customer.FirstName);
        result.Bookings.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnonymizedCustomer_ReturnsDtoWithIsAnonymizedTrue()
    {
        var customer = SomeCustomer();
        customer.Anonymize();

        var (customerRepository, bookingRepository) = MockRepositories();
        customerRepository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);
        bookingRepository.GetForCustomerWithVehicleAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<(Booking, Vehicle)>());

        var handler = new GetCustomerSummaryQueryHandler(customerRepository, bookingRepository);

        var result = await handler.Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        result.IsAnonymized.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DeactivatedCustomer_StillReturnsSummary_NotNotFound()
    {
        // GetByIdIncludingSoftDeletedAsync must be the repository call used here (per Boundaries) --
        // a deactivated customer's summary still renders, not just an active one.
        var customer = SomeCustomer();
        customer.SoftDelete();

        var (customerRepository, bookingRepository) = MockRepositories();
        customerRepository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);
        bookingRepository.GetForCustomerWithVehicleAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<(Booking, Vehicle)>());

        var handler = new GetCustomerSummaryQueryHandler(customerRepository, bookingRepository);

        var result = await handler.Handle(new GetCustomerSummaryQuery(customer.Id), CancellationToken.None);

        result.Id.Should().Be(customer.Id);
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExistAtAll_ThrowsNotFoundException()
    {
        var missingId = Guid.NewGuid();

        var (customerRepository, bookingRepository) = MockRepositories();
        customerRepository.GetByIdIncludingSoftDeletedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var handler = new GetCustomerSummaryQueryHandler(customerRepository, bookingRepository);

        var act = async () => await handler.Handle(new GetCustomerSummaryQuery(missingId), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);
    }
}
