using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Requests that the Vehicle identified by <see cref="VehicleId"/> be restored
/// (<see cref="Domain.Vehicle.Restore"/>). Returns <c>Unit</c> (MediatR's empty-response type),
/// not a DTO -- per AD-2's exception list ("Cancel/SoftDelete/Anonymize/Delete commands... return
/// <c>Unit</c>/204"); Restore is the same shape of mutation.
/// </summary>
public record RestoreVehicleCommand(Guid VehicleId) : IRequest;
