using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Vehicles;

/// <summary>
/// Intention-revealing read/write methods over the Vehicle aggregate. Returns/accepts domain
/// <see cref="Vehicle"/> entities, never DTOs -- mapping to <c>VehicleDto</c> happens in the
/// calling handler, not here (AD-1/AD-5); a repository returning or requiring Application-layer DTO
/// shapes would invert that dependency direction. No method here ever calls
/// <c>SaveChangesAsync</c> -- that is <see cref="Common.IUnitOfWork"/>'s sole responsibility (AD-5),
/// called exactly once by the handler after every repository call for that request has queued its
/// change.
/// </summary>
public interface IVehicleRepository
{
    Task<(IReadOnlyList<Vehicle> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pre-insert/pre-update existence check for <see cref="Commands.CreateVehicleCommandHandler"/>'s
    /// and <see cref="Commands.UpdateVehicleCommandHandler"/>'s duplicate-RegistrationNumber guard
    /// (AD-8). Deliberately ignores the soft-delete query filter -- the database's unique index on
    /// RegistrationNumber is itself unfiltered, so this check must match that same unfiltered scope,
    /// or a soft-deleted vehicle's old registration number would look "free" here yet still throw a
    /// raw <c>DbUpdateException</c> from the DB constraint on insert/update. <paramref name="excludingId"/>,
    /// when non-null, excludes that vehicle's own row from the match -- so editing a vehicle without
    /// changing its RegistrationNumber never flags it as a duplicate of itself. Story 2.1's
    /// Create call site always passes <c>excludingId: null</c>.
    /// </summary>
    Task<bool> ExistsByRegistrationNumberAsync(string registrationNumber, Guid? excludingId, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a <see cref="Vehicle"/> by id, respecting the existing soft-delete query filter
    /// (AD-13) -- consistent with every other read in this repository except the deliberately
    /// unfiltered existence check above. A soft-deleted vehicle is therefore treated as not found
    /// here (see spec-2-2's Design Notes: there is no UI path to a soft-deleted vehicle's Edit flow
    /// yet). Returns <c>null</c> when no matching, non-deleted row exists -- callers map that to a
    /// <c>NotFoundException</c>, never a <c>NullReferenceException</c>.
    /// </summary>
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a new <see cref="Vehicle"/> for insertion. Never calls <c>SaveChangesAsync</c> --
    /// the caller commits via <see cref="Common.IUnitOfWork"/> (AD-5).
    /// </summary>
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);
}
