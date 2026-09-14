using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Queries;

/// <summary>
/// Calls <see cref="IVehicleRepository.GetPagedAsync"/>, maps each domain <c>Vehicle</c> to a
/// <see cref="VehicleDto"/>, and wraps the page in the shared <see cref="PagedResult{T}"/> shape.
/// Never validates <see cref="GetVehiclesQuery"/> itself -- that already happened in the
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> pipeline stage before this handler runs.
/// </summary>
public class GetVehiclesQueryHandler(IVehicleRepository repository)
    : IRequestHandler<GetVehiclesQuery, PagedResult<VehicleDto>>
{
    public async Task<PagedResult<VehicleDto>> Handle(GetVehiclesQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await repository.GetPagedAsync(
            request.Page, request.PageSize, request.Search, request.IncludeInactive, cancellationToken);

        var dtos = items
            .Select(VehicleDto.FromDomain)
            .ToList();

        return new PagedResult<VehicleDto>(dtos, totalCount, request.Page, request.PageSize);
    }
}
