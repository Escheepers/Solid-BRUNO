using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="UpdateVehicleCommand"/>: <see cref="IVehicleRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the vehicle doesn't exist or isn't currently
/// visible) -> a duplicate-RegistrationNumber existence check excluding the vehicle's own id -> the
/// domain mutation (which itself still enforces every invariant, letting
/// <see cref="DomainRuleViolationException"/> propagate unmodified) ->
/// exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <see cref="VehicleDto"/>.
/// Contains no validation logic of its own -- input-shape rules live in
/// <see cref="UpdateVehicleCommandValidator"/> (via the pipeline), domain invariants live in
/// <see cref="Vehicle.Update"/>.
/// </summary>
public class UpdateVehicleCommandHandler(IVehicleRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateVehicleCommand, VehicleDto>
{
    public async Task<VehicleDto> Handle(UpdateVehicleCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await repository.GetByIdAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        var duplicateExists = await repository.ExistsByRegistrationNumberAsync(
            request.RegistrationNumber, request.VehicleId, cancellationToken);

        if (duplicateExists)
        {
            throw new DomainRuleViolationException(
                "Vehicle", "RegistrationNumber", "This registration number is already in use.");
        }

        vehicle.Update(request.RegistrationNumber, request.Make, request.Model, request.Year, request.DailyRate);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VehicleDto.FromDomain(vehicle);
    }
}
