using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="RestoreVehicleCommand"/>:
/// <see cref="IVehicleRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="Vehicle.Restore"/>,
/// letting <see cref="Domain.Exceptions.DomainRuleViolationException"/> propagate unmodified when the
/// vehicle is already active -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Contains no
/// business logic of its own beyond that orchestration (SRP) -- deliberately uses
/// <c>GetByIdIncludingSoftDeletedAsync</c>, not <c>GetByIdAsync</c>, since the latter's query filter
/// would incorrectly 404 every soft-deleted vehicle this command is asked to restore.
/// </summary>
public class RestoreVehicleCommandHandler(IVehicleRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<RestoreVehicleCommand>
{
    public async Task Handle(RestoreVehicleCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await repository.GetByIdIncludingSoftDeletedAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        vehicle.Restore();

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
