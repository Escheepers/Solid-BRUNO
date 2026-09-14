using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Customers;

/// <summary>
/// Intention-revealing read/write methods over the Customer aggregate. Returns/accepts domain
/// <see cref="Customer"/> entities, never DTOs -- mapping to <c>CustomerDto</c> happens in the
/// calling handler, not here (AD-1/AD-5); a repository returning or requiring Application-layer DTO
/// shapes would invert that dependency direction. No method here ever calls
/// <c>SaveChangesAsync</c> -- that is <see cref="Common.IUnitOfWork"/>'s sole responsibility (AD-5),
/// called exactly once by the handler after every repository call for that request has queued its
/// change. Deliberately mirrors <c>IVehicleRepository</c>'s shape, minus every member Story 3.1 has
/// no use for yet (no GetById/Update/SoftDelete/Restore -- those arrive in Stories 3.2+, YAGNI).
/// </summary>
public interface ICustomerRepository
{
    /// <summary>
    /// Search matches FirstName/LastName ONLY -- Email/PhoneNumber are encrypted at rest, and
    /// pattern-matching ciphertext is meaningless (see spec-3-1's Scope decision 2).
    /// </summary>
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
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
    /// shape/reasoning. Customer has no soft-delete/restore yet (Story 3.4), so this is equivalent
    /// to "any customer" today -- a <c>GetByIdIncludingSoftDeletedAsync</c> variant arrives only when
    /// Story 3.4's Restore actually needs one (YAGNI, mirroring Vehicle's exact evolution). Returns
    /// <c>null</c> when no matching row exists -- callers map that to a <c>NotFoundException</c>,
    /// never a <c>NullReferenceException</c>.
    /// </summary>
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a new <see cref="Customer"/> for insertion. Never calls <c>SaveChangesAsync</c> --
    /// the caller commits via <see cref="Common.IUnitOfWork"/> (AD-5).
    /// </summary>
    Task AddAsync(Customer customer, CancellationToken cancellationToken);
}
