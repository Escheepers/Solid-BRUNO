using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Vehicles.Dtos;

/// <summary>
/// The wire-shape projection of a <see cref="Vehicle"/> aggregate returned by every Vehicle query
/// and mutation. Mapping from the domain entity happens only via <see cref="FromDomain"/>, called
/// from handlers -- never in the repository (AD-1/AD-5), which stays ignorant of Application-layer
/// DTO shapes.
/// </summary>
public record VehicleDto(
    Guid Id,
    string RegistrationNumber,
    string Make,
    string Model,
    int Year,
    decimal DailyRate,
    DateTime CreatedDate)
{
    /// <summary>
    /// The one place a <see cref="Vehicle"/> is projected to its wire shape, shared by every
    /// query/command handler that returns a <see cref="VehicleDto"/> (DRY -- this exact projection
    /// previously lived duplicated inline in each handler).
    /// </summary>
    public static VehicleDto FromDomain(Vehicle vehicle) => new(
        vehicle.Id,
        vehicle.RegistrationNumber,
        vehicle.Make,
        vehicle.Model,
        vehicle.Year,
        vehicle.DailyRate,
        vehicle.CreatedDate);
}
