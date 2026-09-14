using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="RestoreVehicleCommandHandler"/>'s orchestration:
/// <c>GetByIdIncludingSoftDeletedAsync</c> (mocked <see cref="IVehicleRepository"/>) -> not-found
/// check -> <c>vehicle.Restore()</c>, letting <see cref="DomainRuleViolationException"/> propagate
/// unmodified -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Written before the handler
/// existed (TDD) -- see spec-2-4's Tasks &amp; Acceptance.
/// </summary>
public class RestoreVehicleCommandHandlerTests
{
    private static Vehicle SoftDeletedVehicle()
    {
        var vehicle = Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);
        vehicle.SoftDelete();
        return vehicle;
    }

    private static Vehicle ActiveVehicle() =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    [Fact]
    public async Task Handle_SoftDeletedVehicle_RestoresAndSavesExactlyOnce()
    {
        var vehicle = SoftDeletedVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdIncludingSoftDeletedAsync(vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(vehicle);

        var handler = new RestoreVehicleCommandHandler(repository, unitOfWork);
        var command = new RestoreVehicleCommand(vehicle.Id);

        await handler.Handle(command, CancellationToken.None);

        vehicle.IsDeleted.Should().BeFalse();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyActiveVehicle_ThrowsDomainRuleViolationException_AndNeverSaves()
    {
        var vehicle = ActiveVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdIncludingSoftDeletedAsync(vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(vehicle);

        var handler = new RestoreVehicleCommandHandler(repository, unitOfWork);
        var command = new RestoreVehicleCommand(vehicle.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Already active.");
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Rule.Should().Be(nameof(Vehicle.IsDeleted));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleDoesNotExistAtAll_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdIncludingSoftDeletedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Vehicle?)null);

        var handler = new RestoreVehicleCommandHandler(repository, unitOfWork);
        var command = new RestoreVehicleCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
