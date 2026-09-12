using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="UpdateVehicleCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="IVehicleRepository"/>) -> not-found check -> duplicate-excluding-self check ->
/// <c>vehicle.Update(...)</c> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped
/// <c>VehicleDto</c>. Critically covers the exclude-self regression: submitting a vehicle's own
/// unchanged RegistrationNumber must NOT be treated as a duplicate of itself.
/// </summary>
public class UpdateVehicleCommandHandlerTests
{
    private static Vehicle ExistingVehicle() =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    private static UpdateVehicleCommand CommandFor(
        Vehicle vehicle,
        string? registrationNumber = null,
        string make = "Toyota",
        string model = "Corolla",
        int year = 2023,
        decimal dailyRate = 400m) =>
        new(vehicle.Id, registrationNumber ?? vehicle.RegistrationNumber, make, model, year, dailyRate);

    [Fact]
    public async Task Handle_ExistingVehicleWithNewRegistrationNumber_UpdatesVehicleAndSavesOnce()
    {
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
        repository.ExistsByRegistrationNumberAsync("CA999999", vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new UpdateVehicleCommandHandler(repository, unitOfWork);
        var command = CommandFor(vehicle, registrationNumber: "CA999999", make: "Honda", model: "Civic", year: 2024, dailyRate: 420m);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Id.Should().Be(vehicle.Id);
        result.RegistrationNumber.Should().Be("CA999999");
        result.Make.Should().Be("Honda");
        result.Model.Should().Be("Civic");
        result.Year.Should().Be(2024);
        result.DailyRate.Should().Be(420m);

        vehicle.RegistrationNumber.Should().Be("CA999999");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SubmittingVehiclesOwnUnchangedRegistrationNumber_DoesNotThrow()
    {
        // Regression: excludingId must be passed correctly (the vehicle's own id), not omitted or
        // inverted -- otherwise a vehicle's own current RegistrationNumber would be flagged as a
        // duplicate of itself.
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
        repository.ExistsByRegistrationNumberAsync(vehicle.RegistrationNumber, vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new UpdateVehicleCommandHandler(repository, unitOfWork);
        var command = CommandFor(vehicle, dailyRate: 500m);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().NotThrowAsync();
        vehicle.DailyRate.Should().Be(500m);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateRegistrationNumberAgainstDifferentVehicle_ThrowsDomainRuleViolationException_AndNeverSaves()
    {
        var vehicle = ExistingVehicle();
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
        repository.ExistsByRegistrationNumberAsync("CA999999", vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new UpdateVehicleCommandHandler(repository, unitOfWork);
        var command = CommandFor(vehicle, registrationNumber: "CA999999");

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("This registration number is already in use.");
        exception.Which.Entity.Should().Be("Vehicle");
        exception.Which.Rule.Should().Be("RegistrationNumber");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VehicleDoesNotExist_ThrowsNotFoundException_AndNeverChecksDuplicateOrSaves()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Vehicle?)null);

        var handler = new UpdateVehicleCommandHandler(repository, unitOfWork);
        var command = new UpdateVehicleCommand(missingId, "CA123456", "Toyota", "Corolla", 2023, 350m);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Id.Should().Be(missingId);

        await repository.DidNotReceive().ExistsByRegistrationNumberAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
