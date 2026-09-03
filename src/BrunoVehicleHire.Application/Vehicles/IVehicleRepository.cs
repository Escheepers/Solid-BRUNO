using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Vehicles;

/// <summary>
/// One intention-revealing paged read method. Returns domain <see cref="Vehicle"/> entities, never
/// DTOs -- mapping to <c>VehicleDto</c> happens in the query handler, not here (AD-1/AD-5); a
/// repository returning Application-layer DTOs would invert that dependency direction.
/// </summary>
public interface IVehicleRepository
{
    Task<(IReadOnlyList<Vehicle> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken);
}
