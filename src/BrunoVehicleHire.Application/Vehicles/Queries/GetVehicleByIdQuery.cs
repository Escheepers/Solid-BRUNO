using BrunoVehicleHire.Application.Vehicles.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Queries;

/// <summary>
/// Requests a single Vehicle's full record by id, for the detail view (Story 2.5) -- including a
/// soft-deleted vehicle's, since the AC requires showing its active/soft-deleted state.
/// </summary>
public record GetVehicleByIdQuery(Guid VehicleId) : IRequest<VehicleDto>;
