using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Application.Vehicles.Queries;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Vehicles;

/// <summary>
/// Proves <see cref="GetVehicleByIdQueryHandler"/>'s orchestration:
/// <c>GetByIdIncludingSoftDeletedAsync</c> (mocked <see cref="IVehicleRepository"/>) -> not-found
/// check -> <c>VehicleDto.FromDomain</c>. Written before the handler existed (TDD) -- see
/// spec-2-5's Tasks &amp; Acceptance. Deliberately uses <c>GetByIdIncludingSoftDeletedAsync</c>, not
/// <c>GetByIdAsync</c>, so a soft-deleted vehicle's detail can still be viewed (spec-2-5 Scope
/// decision 1).
/// </summary>
public class GetVehicleByIdQueryHandlerTests
{
    private static Vehicle ActiveVehicle() =>
        Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);

    private static Vehicle SoftDeletedVehicle()
    {
        var vehicle = Vehicle.Create("CA654321", "Honda", "Civic", 2024, 400m);
        vehicle.SoftDelete();
        return vehicle;
    }

    [Fact]
    public async Task Handle_ActiveVehicleExists_ReturnsCorrectlyMappedDto()
    {
        var vehicle = ActiveVehicle();
        var repository = Substitute.For<IVehicleRepository>();

        repository.GetByIdIncludingSoftDeletedAsync(vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(vehicle);

        var handler = new GetVehicleByIdQueryHandler(repository);

        var result = await handler.Handle(new GetVehicleByIdQuery(vehicle.Id), CancellationToken.None);

        result.Id.Should().Be(vehicle.Id);
        result.RegistrationNumber.Should().Be(vehicle.RegistrationNumber);
        result.Make.Should().Be(vehicle.Make);
        result.Model.Should().Be(vehicle.Model);
        result.Year.Should().Be(vehicle.Year);
        result.DailyRate.Should().Be(vehicle.DailyRate);
        result.CreatedDate.Should().Be(vehicle.CreatedDate);
        result.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SoftDeletedVehicleExists_ReturnsDtoWithIsDeletedTrue()
    {
        var vehicle = SoftDeletedVehicle();
        var repository = Substitute.For<IVehicleRepository>();

        repository.GetByIdIncludingSoftDeletedAsync(vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(vehicle);

        var handler = new GetVehicleByIdQueryHandler(repository);

        var result = await handler.Handle(new GetVehicleByIdQuery(vehicle.Id), CancellationToken.None);

        result.Id.Should().Be(vehicle.Id);
        result.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_VehicleDoesNotExistAtAll_ThrowsNotFoundException()
    {
        var repository = Substitute.For<IVehicleRepository>();
        var missingId = Guid.NewGuid();

        repository.GetByIdIncludingSoftDeletedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Vehicle?)null);

        var handler = new GetVehicleByIdQueryHandler(repository);

        var act = async () => await handler.Handle(new GetVehicleByIdQuery(missingId), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Vehicle));
        exception.Which.Id.Should().Be(missingId);
    }
}
