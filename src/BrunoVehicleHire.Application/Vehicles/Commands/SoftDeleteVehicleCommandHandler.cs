using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="SoftDeleteVehicleCommand"/>: <see cref="IVehicleRepository.GetByIdAsync"/>
/// (throwing <see cref="NotFoundException"/> if the vehicle doesn't exist or isn't currently
/// visible -- including an already-soft-deleted vehicle, since the query filter still applies) ->
/// <see cref="Vehicle.SoftDelete"/> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// Contains no business logic of its own beyond that orchestration (SRP) -- the domain method itself
/// is idempotent, but this handler never reaches it a second time for an already-deleted vehicle
/// since <c>GetByIdAsync</c> already treats it as not found.
/// </summary>
public class SoftDeleteVehicleCommandHandler(IVehicleRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<SoftDeleteVehicleCommand>
{
    public async Task Handle(SoftDeleteVehicleCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await repository.GetByIdAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        vehicle.SoftDelete();

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
