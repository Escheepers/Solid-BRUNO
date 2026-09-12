using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="CreateVehicleCommandHandler"/>'s orchestration: existence check (mocked
/// <see cref="IVehicleRepository"/>) -> domain factory -> <c>AddAsync</c> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <c>VehicleDto</c>; and that a duplicate
/// RegistrationNumber short-circuits before any persistence call, throwing
/// <see cref="DomainRuleViolationException"/> with the exact AC message.
/// </summary>
public class CreateVehicleCommandHandlerTests
{
    private static CreateVehicleCommand ValidCommand() =>
        new("CA123456", "Toyota", "Corolla", 2023, 350m);

    [Fact]
    public async Task Handle_NewRegistrationNumber_ReturnsMappedDtoAndSavesOnce()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.ExistsByRegistrationNumberAsync("CA123456", Arg.Any<CancellationToken>())
            .Returns(false);

        Vehicle? addedVehicle = null;
        await repository.AddAsync(Arg.Do<Vehicle>(v => addedVehicle = v), Arg.Any<CancellationToken>());

        var handler = new CreateVehicleCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        addedVehicle.Should().NotBeNull();
        result.Id.Should().Be(addedVehicle!.Id);
        result.RegistrationNumber.Should().Be("CA123456");
        result.Make.Should().Be("Toyota");
        result.Model.Should().Be("Corolla");
        result.Year.Should().Be(2023);
        result.DailyRate.Should().Be(350m);
        result.CreatedDate.Should().Be(addedVehicle.CreatedDate);

        await repository.Received(1).AddAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateRegistrationNumber_ThrowsDomainRuleViolationException_AndNeverPersists()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.ExistsByRegistrationNumberAsync("CA123456", Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new CreateVehicleCommandHandler(repository, unitOfWork);

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("This registration number is already in use.");
        exception.Which.Entity.Should().Be("Vehicle");
        exception.Which.Rule.Should().Be("RegistrationNumber");

        await repository.DidNotReceive().AddAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
