using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="SoftDeleteVehicleCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="IVehicleRepository"/>) -> not-found check -> <c>vehicle.SoftDelete()</c> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. Written before the handler existed (TDD) -- see
/// spec-2-3's Tasks &amp; Acceptance.
/// </summary>
public class SoftDeleteVehicleCommandHandlerTests
{
    private static Vehicle ExistingVehicle() =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    [Fact]
    public async Task Handle_ExistingVehicle_CallsSoftDeleteAndSavesExactlyOnce()
    {
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);

        var handler = new SoftDeleteVehicleCommandHandler(repository, unitOfWork);
        var command = new SoftDeleteVehicleCommand(vehicle.Id);

        await handler.Handle(command, CancellationToken.None);

        vehicle.IsDeleted.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleDoesNotExist_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Vehicle?)null);

        var handler = new SoftDeleteVehicleCommandHandler(repository, unitOfWork);
        var command = new SoftDeleteVehicleCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
