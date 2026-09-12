using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="CreateVehicleCommand"/>: a pre-insert duplicate-RegistrationNumber
/// existence check (AD-8's own named example) -> the domain factory (which itself still enforces
/// the Year plausible-range invariant, letting <see cref="DomainRuleViolationException"/> propagate
/// unmodified) -> <see cref="IVehicleRepository.AddAsync"/> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <see cref="VehicleDto"/>. Contains no
/// validation logic of its own -- input-shape rules live in
/// <see cref="CreateVehicleCommandValidator"/> (via the pipeline), domain invariants live in
/// <see cref="Vehicle.Create"/>.
/// </summary>
public class CreateVehicleCommandHandler(IVehicleRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateVehicleCommand, VehicleDto>
{
    public async Task<VehicleDto> Handle(CreateVehicleCommand request, CancellationToken cancellationToken)
    {
        var alreadyExists = await repository.ExistsByRegistrationNumberAsync(
            request.RegistrationNumber, excludingId: null, cancellationToken);

        if (alreadyExists)
        {
            throw new DomainRuleViolationException(
                "Vehicle", "RegistrationNumber", "This registration number is already in use.");
        }

        var vehicle = Vehicle.Create(
            request.RegistrationNumber, request.Make, request.Model, request.Year, request.DailyRate);

        await repository.AddAsync(vehicle, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VehicleDto.FromDomain(vehicle);
    }
}
