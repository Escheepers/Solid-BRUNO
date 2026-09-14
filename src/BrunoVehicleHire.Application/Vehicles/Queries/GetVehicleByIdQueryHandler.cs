using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Queries;

/// <summary>
/// Orchestrates <see cref="GetVehicleByIdQuery"/>:
/// <see cref="IVehicleRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="VehicleDto.FromDomain"/>.
/// Contains no business logic of its own beyond that orchestration (SRP) -- deliberately uses
/// <c>GetByIdIncludingSoftDeletedAsync</c>, not <c>GetByIdAsync</c>, since the detail page's AC
/// requires showing a soft-deleted vehicle's state too, which the filtered lookup would incorrectly
/// 404 (spec-2-5 Scope decision 1).
/// </summary>
public class GetVehicleByIdQueryHandler(IVehicleRepository repository)
    : IRequestHandler<GetVehicleByIdQuery, VehicleDto>
{
    public async Task<VehicleDto> Handle(GetVehicleByIdQuery request, CancellationToken cancellationToken)
    {
        var vehicle = await repository.GetByIdIncludingSoftDeletedAsync(request.VehicleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), request.VehicleId);

        return VehicleDto.FromDomain(vehicle);
    }
}
