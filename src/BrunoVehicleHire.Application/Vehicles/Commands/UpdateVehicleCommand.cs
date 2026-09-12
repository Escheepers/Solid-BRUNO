using BrunoVehicleHire.Application.Vehicles.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Requests an update to the existing Vehicle identified by <see cref="VehicleId"/>. Returns the
/// updated <see cref="VehicleDto"/> -- never a bare id (AD-2: every mutating Command that
/// creates/updates an aggregate returns its DTO). Validated by
/// <see cref="UpdateVehicleCommandValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler. The API's <c>PUT</c> controller action always overrides
/// <see cref="VehicleId"/> from the route segment, never trusting a client-supplied body value for it.
/// </summary>
public record UpdateVehicleCommand(
    Guid VehicleId,
    string RegistrationNumber,
    string Make,
    string Model,
    int Year,
    decimal DailyRate) : IRequest<VehicleDto>;
