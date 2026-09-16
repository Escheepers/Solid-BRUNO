using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="SoftDeleteVehicleCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="IVehicleRepository"/>) -> not-found check -> has-active-booking check (mocked
/// <see cref="IBookingRepository"/>, 409 if true --
/// spec-vehicle-deactivate-blocked-by-active-bookings) -> <c>vehicle.SoftDelete()</c> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. Written before the handler existed (TDD) -- see
/// spec-2-3's Tasks &amp; Acceptance; the active-booking guard tests were added test-first for
/// spec-vehicle-deactivate-blocked-by-active-bookings, mirroring
/// <c>HardDeleteCustomerCommandHandlerTests</c>'s exact shape.
/// </summary>
public class SoftDeleteVehicleCommandHandlerTests
{
    private static Vehicle ExistingVehicle() =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    [Fact]
    public async Task Handle_ExistingVehicle_NoActiveBooking_CallsSoftDeleteAndSavesExactlyOnce()
    {
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
        bookingRepository.ExistsActiveForVehicleAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new SoftDeleteVehicleCommandHandler(repository, bookingRepository, unitOfWork);
        var command = new SoftDeleteVehicleCommand(vehicle.Id);

        await handler.Handle(command, CancellationToken.None);

        vehicle.IsDeleted.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleDoesNotExist_ThrowsNotFoundException_AndNeverChecksBookingsOrSaves()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Vehicle?)null);

        var handler = new SoftDeleteVehicleCommandHandler(repository, bookingRepository, unitOfWork);
        var command = new SoftDeleteVehicleCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Id.Should().Be(missingId);

        await bookingRepository.DidNotReceive().ExistsActiveForVehicleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleHasActiveBooking_ThrowsDomainRuleViolationException_AndNeverSoftDeletesOrSaves()
    {
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
        bookingRepository.ExistsActiveForVehicleAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new SoftDeleteVehicleCommandHandler(repository, bookingRepository, unitOfWork);
        var command = new SoftDeleteVehicleCommand(vehicle.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be(
            "This vehicle has an active or upcoming booking — cancel it first, or wait for it to complete.");
        exception.Which.Entity.Should().Be("Vehicle");
        exception.Which.Rule.Should().Be("HasActiveBookings");

        vehicle.IsDeleted.Should().BeFalse();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleHasOnlyCompletedOrCancelledBookings_CallsSoftDeleteAndSavesExactlyOnce()
    {
        // ExistsActiveForVehicleAsync's own contract is to only ever return true for an Active
        // booking (spec-vehicle-deactivate-blocked-by-active-bookings) -- this proves the handler
        // side of that contract: fed false (as the repository would genuinely return when the
        // vehicle's bookings are all Completed/Cancelled), the handler proceeds exactly as if there
        // were no bookings at all.
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
        bookingRepository.ExistsActiveForVehicleAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new SoftDeleteVehicleCommandHandler(repository, bookingRepository, unitOfWork);
        var command = new SoftDeleteVehicleCommand(vehicle.Id);

        await handler.Handle(command, CancellationToken.None);

        vehicle.IsDeleted.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
