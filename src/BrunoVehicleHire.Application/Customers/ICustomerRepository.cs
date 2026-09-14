using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Customers;

/// <summary>
/// Intention-revealing read/write methods over the Customer aggregate. Returns/accepts domain
/// <see cref="Customer"/> entities, never DTOs -- mapping to <c>CustomerDto</c> happens in the
/// calling handler, not here (AD-1/AD-5); a repository returning or requiring Application-layer DTO
/// shapes would invert that dependency direction. No method here ever calls
/// <c>SaveChangesAsync</c> -- that is <see cref="Common.IUnitOfWork"/>'s sole responsibility (AD-5),
/// called exactly once by the handler after every repository call for that request has queued its
/// change. Deliberately mirrors <c>IVehicleRepository</c>'s shape, minus every member earlier
/// Customer stories had no use for yet (YAGNI); <c>GetByIdIncludingSoftDeletedAsync</c> and
/// <c>GetPagedAsync</c>'s <c>includeInactive</c> parameter arrive in Story 3.4, mirroring
/// <c>IVehicleRepository</c>'s exact Story-2.4 addition.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>
    /// Search matches FirstName/LastName ONLY -- Email/PhoneNumber are encrypted at rest, and
    /// pattern-matching ciphertext is meaningless (see spec-3-1's Scope decision 2).
    /// <paramref name="includeInactive"/>, when true, ignores the soft-delete query filter for this
    /// call only (the Customers list's "show inactive" toggle -- Story 3.4) -- every other read in
    /// this repository stays filtered. Mirrors <c>IVehicleRepository.GetPagedAsync</c>'s exact
    /// Story-2.4 addition.
    /// </summary>
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pre-insert existence check for <see cref="Commands.CreateCustomerCommandHandler"/>'s
    /// duplicate-Email guard, mirroring <c>IVehicleRepository.ExistsByRegistrationNumberAsync</c>'s
    /// exact reasoning: computes the same normalized hash from the incoming plaintext
    /// <paramref name="email"/> and compares it against the stored EmailHash shadow property.
    /// Deliberately ignores the soft-delete query filter -- the database's unique index on
    /// EmailHash is itself unfiltered, so this check must match that same unfiltered scope, or a
    /// soft-deleted customer's old email would look "free" here yet still throw a raw
    /// <c>DbUpdateException</c> from the DB constraint on insert. <paramref name="excludingId"/>,
    /// when non-null, excludes that customer's own row from the match -- built now (unused,
    /// always <c>null</c>, by this story's Create-only call site) so Story 3.2's Edit doesn't need a
    /// breaking signature change.
    /// </summary>
    Task<bool> ExistsByEmailAsync(string email, Guid? excludingId, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a <see cref="Customer"/> by id, respecting the existing soft-delete query filter
    /// (AD-13) -- consistent with every other read in this repository except the deliberately
    /// unfiltered existence check above. Mirrors <c>IVehicleRepository.GetByIdAsync</c>'s exact
    /// shape/reasoning. A soft-deleted customer is therefore treated as not found here --
    /// <see cref="Commands.SoftDeleteCustomerCommandHandler"/> relies on exactly that so a second
    /// deactivate attempt 404s instead of silently double-processing. Returns <c>null</c> when no
    /// matching row exists -- callers map that to a <c>NotFoundException</c>, never a
    /// <c>NullReferenceException</c>.
    /// </summary>
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a <see cref="Customer"/> by id, deliberately IGNORING the soft-delete query filter
    /// (Story 3.4) -- <see cref="Commands.RestoreCustomerCommandHandler"/>'s whole point is finding a
    /// currently-soft-deleted row, which <see cref="GetByIdAsync"/> would incorrectly treat as not
    /// found. Mirrors <c>IVehicleRepository.GetByIdIncludingSoftDeletedAsync</c>'s exact Story-2.4
    /// addition. Returns <c>null</c> only when no row with this id exists at all (deleted or not).
    /// </summary>
    Task<Customer?> GetByIdIncludingSoftDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a new <see cref="Customer"/> for insertion. Never calls <c>SaveChangesAsync</c> --
    /// the caller commits via <see cref="Common.IUnitOfWork"/> (AD-5).
    /// </summary>
    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    /// <summary>
    /// Queues an existing <see cref="Customer"/> for genuine, permanent deletion (Story 3.3) -- the
    /// first real hard delete anywhere in this repository layer; every prior delete has been a
    /// soft-delete field flip via <c>Update</c>-style methods instead. Mirrors <see cref="AddAsync"/>'s
    /// shape: never calls <c>SaveChangesAsync</c> -- the caller commits via
    /// <see cref="Common.IUnitOfWork"/> (AD-5).
    /// </summary>
    Task RemoveAsync(Customer customer, CancellationToken cancellationToken);
}
