using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Bookings;

/// <summary>
/// Intention-revealing read/write access over the Booking aggregate, mirroring
/// <c>IVehicleRepository</c>/<c>ICustomerRepository</c>'s shape: only what a story has actually
/// needed so far (YAGNI) -- Epic 4 adds the rest (GetById/overlap checks/etc.) only when it actually
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

    /// <summary>
    /// One page of bookings, each joined with its referenced <see cref="Vehicle"/>/
    /// <see cref="Customer"/> row for <see cref="Dtos.BookingDto.FromDomain"/> to project (spec-4-1's
    /// Boundaries: Booking itself carries no navigation properties -- AD-1). Deliberately takes no
    /// <c>search</c>/<c>includeInactive</c> parameter, unlike <c>IVehicleRepository</c>/
    /// <c>ICustomerRepository</c>'s <c>GetPagedAsync</c> (Scope decision 3) -- every booking is
    /// always returned regardless of its Vehicle/Customer's active state (Scope decision 4).
    /// </summary>
    Task<(IReadOnlyList<(Booking Booking, Vehicle Vehicle, Customer Customer)> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Queues a new <see cref="Booking"/> for insertion. Never calls <c>SaveChangesAsync</c> -- the
    /// caller commits via <see cref="Common.IUnitOfWork"/> (AD-5). Mirrors
    /// <c>IVehicleRepository.AddAsync</c>/<c>ICustomerRepository.AddAsync</c>'s exact shape.
    /// </summary>
    Task AddAsync(Booking booking, CancellationToken cancellationToken);
}
