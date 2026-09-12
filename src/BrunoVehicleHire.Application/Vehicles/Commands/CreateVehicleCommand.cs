using BrunoVehicleHire.Application.Vehicles.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Requests creation of a new Vehicle. Returns the created <see cref="VehicleDto"/> -- never a bare
/// id (AD-2: every mutating Command that creates/updates an aggregate returns its DTO). Validated by
/// <see cref="CreateVehicleCommandValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler.
/// </summary>
public record CreateVehicleCommand(
    string RegistrationNumber,
    string Make,
    string Model,
    int Year,
    decimal DailyRate) : IRequest<VehicleDto>;
