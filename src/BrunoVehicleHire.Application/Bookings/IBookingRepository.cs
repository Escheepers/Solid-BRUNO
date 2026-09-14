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

    /// <summary>
    /// Every Booking for <paramref name="vehicleId"/> whose <c>Status</c> is not
    /// <see cref="BookingStatus.Cancelled"/> (spec-4-2): a Cancelled booking's date range never
    /// blocks a new booking, but a Completed one's historical range still does (AD-7's own clause),
    /// so this deliberately does not filter down to only <see cref="BookingStatus.Active"/>.
    /// <see cref="Commands.CreateBookingCommandHandler"/> is the sole caller -- it reads this small
    /// result set into memory and runs the real <see cref="DateRange.Overlaps"/> on each row in plain
    /// C# (spec-4-2's Scope decision 2), rather than this method returning a SQL-translated overlap
    /// predicate.
    /// </summary>
    Task<IReadOnlyList<Booking>> GetNonCancelledForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    /// <summary>
    /// A plain, direct lookup by id -- backs <see cref="Commands.CancelBookingCommandHandler"/>
    /// (spec-4-3), mirroring <c>IVehicleRepository.GetByIdAsync</c>/<c>ICustomerRepository.GetByIdAsync</c>'s
    /// shape. Unlike those, Booking has no soft-delete query filter of its own, so this is a direct,
    /// unfiltered lookup -- no <c>IgnoreQueryFilters()</c> consideration applies here.
    /// </summary>
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every Booking whose <c>Status</c> is <see cref="BookingStatus.Active"/> and whose
    /// <see cref="Booking.EndDate"/> is on or before <paramref name="asOf"/> -- the sweep-eligible set
    /// (spec-4-4), mirroring <see cref="Booking.Cancel"/>'s own ineligibility boundary exactly (one
    /// boundary, never redefined twice). Already-<see cref="BookingStatus.Completed"/> and
    /// already-<see cref="BookingStatus.Cancelled"/> bookings are never selected, regardless of
    /// <see cref="Booking.EndDate"/>. <c>BookingCompletionSweepService.RunSweepAsync</c> is the sole
    /// caller -- no ordering guarantee needed since every eligible booking in the result is dispatched.
    /// </summary>
    Task<IReadOnlyList<Booking>> GetActivePastEndDateAsync(DateOnly asOf, CancellationToken cancellationToken);
}
