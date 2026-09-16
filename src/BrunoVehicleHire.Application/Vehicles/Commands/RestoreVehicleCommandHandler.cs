using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="RestoreVehicleCommand"/>:
/// <see cref="IVehicleRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="Vehicle.Restore"/>,
/// letting <see cref="Domain.Exceptions.DomainRuleViolationException"/> propagate unmodified when the
/// vehicle is already active -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>, wrapped so a
/// <see cref="DbUpdateConcurrencyException"/> from the xmin concurrency token (two near-simultaneous
/// requests both read the pre-mutation row) re-fetches the now-current row and re-invokes
/// <see cref="Vehicle.Restore"/>, letting IT throw the correct exception for the true current state
/// rather than silently double-processing. Contains no business logic of its own beyond that
/// orchestration (SRP) -- deliberately uses <c>GetByIdIncludingSoftDeletedAsync</c>, not
/// <c>GetByIdAsync</c>, since the latter's query filter would incorrectly 404 every soft-deleted
/// vehicle this command is asked to restore.
/// </summary>
public class RestoreVehicleCommandHandler(IVehicleRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<RestoreVehicleCommand>
{
    public async Task Handle(RestoreVehicleCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await repository.GetByIdIncludingSoftDeletedAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        vehicle.Restore();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rare race this catch exists for: two near-simultaneous requests (e.g. a
            // double-click) both read the pre-mutation row before either committed. Re-fetching and
            // re-calling Restore() lets the domain method see the true post-mutation state and throw
            // its own correct DomainRuleViolationException -- never a hand-written duplicate message.
            var current = await repository.GetByIdIncludingSoftDeletedAsync(request.VehicleId, cancellationToken)
                ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

            current.Restore();

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
