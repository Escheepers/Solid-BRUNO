using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// What <see cref="SoftDeleteVehicleCommandHandler"/> and <see cref="UpdateVehicleCommandHandler"/> do
/// when the Vehicle's xmin concurrency token rejects their save (<see cref="DbUpdateConcurrencyException"/>:
/// another request changed the row between this request's read and write). Before this fix neither
/// handler caught it, so a double-clicked Deactivate (or an Edit racing another change) surfaced as a 500.
/// Mirrors the existing Restore/Cancel handlers' intent: a clean 4xx that reflects the true state.
/// </summary>
public class VehicleConcurrencyConflictTests
{
    private static Vehicle ActiveVehicle() =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    private static IUnitOfWork FailingUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException());
        return unitOfWork;
    }

    [Fact]
    public async Task SoftDelete_LostTheRace_VehicleNowDeactivated_ThrowsNotFound_LikeASequentialSecondClick()
    {
        var vehicle = ActiveVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();

        // First read finds the active vehicle; after the winner commits, the filtered re-read finds nothing.
        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle, (Vehicle?)null);

        var handler = new SoftDeleteVehicleCommandHandler(repository, bookingRepository, FailingUnitOfWork());

        var act = async () => await handler.Handle(new SoftDeleteVehicleCommand(vehicle.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Id.Should().Be(vehicle.Id);
    }

    [Fact]
    public async Task SoftDelete_ConflictFromSomeOtherChange_VehicleStillActive_ThrowsConcurrencyConflict409()
    {
        var vehicle = ActiveVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);

        var handler = new SoftDeleteVehicleCommandHandler(repository, bookingRepository, FailingUnitOfWork());

        var act = async () => await handler.Handle(new SoftDeleteVehicleCommand(vehicle.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Rule.Should().Be("ConcurrencyConflict");
        exception.Which.Message.Should().Be("This vehicle was changed by someone else just now. Reload and try again.");
    }

    [Fact]
    public async Task Update_LostTheRace_VehicleNoLongerVisible_ThrowsNotFound()
    {
        var vehicle = ActiveVehicle();
        var repository = Substitute.For<IVehicleRepository>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle, (Vehicle?)null);

        var handler = new UpdateVehicleCommandHandler(repository, FailingUnitOfWork());

        var act = async () => await handler.Handle(
            new UpdateVehicleCommand(vehicle.Id, "CA123456", "Toyota", "Corolla", 2023, 400m),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_ConflictWhileVehicleStillVisible_ThrowsConcurrencyConflict409()
    {
        var vehicle = ActiveVehicle();
        var repository = Substitute.For<IVehicleRepository>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);

        var handler = new UpdateVehicleCommandHandler(repository, FailingUnitOfWork());

        var act = async () => await handler.Handle(
            new UpdateVehicleCommand(vehicle.Id, "CA123456", "Toyota", "Corolla", 2023, 400m),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Rule.Should().Be("ConcurrencyConflict");
    }
}
