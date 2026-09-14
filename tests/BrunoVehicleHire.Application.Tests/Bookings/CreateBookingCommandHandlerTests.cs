using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Proves <see cref="CreateBookingCommandHandler"/>'s orchestration: Vehicle lookup (including
/// soft-deleted rows) -> Customer lookup (filtered) -> server-computed <c>TotalPrice</c> -> the
/// domain factory -> <c>AddAsync</c> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> ->
/// mapped <c>BookingDto</c>; and every I/O-matrix error row from spec-4-1: a soft-deleted vehicle
/// throws <see cref="DomainRuleViolationException"/>/409 with the exact AC message, a nonexistent
/// vehicle or nonexistent/inactive customer throws <see cref="NotFoundException"/>/404 -- none of
/// which ever reaches <c>AddAsync</c>/<c>SaveChangesAsync</c>.
/// </summary>
public class CreateBookingCommandHandlerTests
{
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 10, 1);
    private static readonly DateOnly EndDate = new(2026, 10, 5); // 4 days after StartDate

    private static CreateBookingCommand ValidCommand() => new(VehicleId, CustomerId, StartDate, EndDate);

    private static Vehicle ActiveVehicle(decimal dailyRate = 350m) =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, dailyRate);

    private static Customer ActiveCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    private static (IVehicleRepository VehicleRepository, ICustomerRepository CustomerRepository,
        IBookingRepository BookingRepository, IUnitOfWork UnitOfWork) Substitutes() => (
            Substitute.For<IVehicleRepository>(),
            Substitute.For<ICustomerRepository>(),
            Substitute.For<IBookingRepository>(),
            Substitute.For<IUnitOfWork>());

    [Fact]
    public async Task Handle_ValidVehicleAndCustomer_ComputesTotalPriceAndSavesOnce()
    {
        var (vehicleRepository, customerRepository, bookingRepository, unitOfWork) = Substitutes();

        var vehicle = ActiveVehicle(dailyRate: 350m);
        var customer = ActiveCustomer();

        vehicleRepository.GetByIdIncludingSoftDeletedAsync(VehicleId, Arg.Any<CancellationToken>())
            .Returns(vehicle);
        customerRepository.GetByIdAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(customer);

        Booking? addedBooking = null;
        await bookingRepository.AddAsync(Arg.Do<Booking>(b => addedBooking = b), Arg.Any<CancellationToken>());

        var handler = new CreateBookingCommandHandler(
            vehicleRepository, customerRepository, bookingRepository, unitOfWork);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        addedBooking.Should().NotBeNull();
        result.Id.Should().Be(addedBooking!.Id);
        result.VehicleId.Should().Be(vehicle.Id);
        result.CustomerId.Should().Be(customer.Id);
        result.Status.Should().Be(BookingStatus.Active);
        // 4 days x 350 = 1400.
        result.TotalPrice.Should().Be(1400m);
        result.VehicleMake.Should().Be("Toyota");
        result.CustomerFirstName.Should().Be("Jane");

        await bookingRepository.Received(1).AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SoftDeletedVehicle_ThrowsDomainRuleViolationException_AndNeverPersists()
    {
        var (vehicleRepository, customerRepository, bookingRepository, unitOfWork) = Substitutes();

        var vehicle = ActiveVehicle();
        vehicle.SoftDelete();

        vehicleRepository.GetByIdIncludingSoftDeletedAsync(VehicleId, Arg.Any<CancellationToken>())
            .Returns(vehicle);

        var handler = new CreateBookingCommandHandler(
            vehicleRepository, customerRepository, bookingRepository, unitOfWork);

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("This vehicle is not available.");
        exception.Which.Entity.Should().Be("Vehicle");

        await customerRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await bookingRepository.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonexistentVehicle_ThrowsNotFoundException_AndNeverPersists()
    {
        var (vehicleRepository, customerRepository, bookingRepository, unitOfWork) = Substitutes();

        vehicleRepository.GetByIdIncludingSoftDeletedAsync(VehicleId, Arg.Any<CancellationToken>())
            .Returns((Vehicle?)null);

        var handler = new CreateBookingCommandHandler(
            vehicleRepository, customerRepository, bookingRepository, unitOfWork);

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();

        await bookingRepository.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonexistentOrInactiveCustomer_ThrowsNotFoundException_AndNeverPersists()
    {
        var (vehicleRepository, customerRepository, bookingRepository, unitOfWork) = Substitutes();

        var vehicle = ActiveVehicle();
        vehicleRepository.GetByIdIncludingSoftDeletedAsync(VehicleId, Arg.Any<CancellationToken>())
            .Returns(vehicle);
        customerRepository.GetByIdAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var handler = new CreateBookingCommandHandler(
            vehicleRepository, customerRepository, bookingRepository, unitOfWork);

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();

        await bookingRepository.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
