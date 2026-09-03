namespace BrunoVehicleHire.Application.Vehicles.Dtos;

/// <summary>
/// The wire-shape projection of a <see cref="BrunoVehicleHire.Domain.Vehicle"/> aggregate returned
/// by every Vehicle query. Mapping from the domain entity happens in the query handler, never in
/// the repository (AD-1/AD-5) -- the repository stays ignorant of Application-layer DTO shapes.
/// </summary>
public record VehicleDto(
    Guid Id,
    string RegistrationNumber,
    string Make,
    string Model,
    int Year,
    decimal DailyRate,
    DateTime CreatedDate);
