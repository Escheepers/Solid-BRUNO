namespace BrunoVehicleHire.Application.Common;

/// <summary>
/// Thrown when a requested aggregate does not exist for the given id (or, for an entity guarded by
/// a soft-delete query filter, is not currently visible -- see <c>VehicleRepository.GetByIdAsync</c>'s
/// deliberate choice to respect that filter). Framework-free, Application-layer only; mapped to a
/// <c>404 Not Found</c> by <see cref="Api.ExceptionHandling.GlobalExceptionHandler"/>. Deliberately a
/// small, specific type -- not a generic multi-purpose "destructive"/error umbrella exception.
/// </summary>
public class NotFoundException(string entity, Guid id) : Exception($"{entity} '{id}' was not found.")
{
    public string Entity { get; } = entity;

    public Guid Id { get; } = id;
}
