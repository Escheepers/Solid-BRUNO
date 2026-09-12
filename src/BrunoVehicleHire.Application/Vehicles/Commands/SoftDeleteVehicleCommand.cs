using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Requests that the Vehicle identified by <see cref="VehicleId"/> be soft-deleted
/// (<see cref="Domain.Vehicle.SoftDelete"/>). Returns <c>Unit</c> (MediatR's empty-response type),
/// not a DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/Delete commands... return
/// <c>Unit</c>/204").
/// </summary>
public record SoftDeleteVehicleCommand(Guid VehicleId) : IRequest;
