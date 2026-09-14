using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Queries;

/// <summary>
/// Requests one page of vehicles, optionally filtered by a free-text search across
/// Make/Model/RegistrationNumber. Validated by <see cref="GetVehiclesQueryValidator"/> through the
/// shared <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated
/// manually in the handler.
/// </summary>
public record GetVehiclesQuery(int Page, int PageSize, string? Search, bool IncludeInactive = false)
    : IRequest<PagedResult<VehicleDto>>;
