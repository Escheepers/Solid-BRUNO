namespace BrunoVehicleHire.Application.Bookings;

/// <summary>
/// Intention-revealing read access over the Booking aggregate. Exactly one method, mirroring how
/// <c>IVehicleRepository</c>/<c>ICustomerRepository</c> both started with only what their first story
/// needed (YAGNI) -- Epic 4 adds the rest (GetById/Add/overlap checks/etc.) only when it actually
/// needs them. No method here ever calls <c>SaveChangesAsync</c> -- that is
/// <see cref="Common.IUnitOfWork"/>'s sole responsibility (AD-5).
/// </summary>
public interface IBookingRepository
{
    /// <summary>
    /// The guard query behind <see cref="Customers.Commands.HardDeleteCustomerCommandHandler"/>'s
    /// has-bookings check (spec-3-3): true if any Booking row (of any <c>Status</c>) references
    /// <paramref name="customerId"/>, per domain-model.md's "a customer cannot be deleted while any
    /// booking (of any status) references it".
    /// </summary>
    Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}
