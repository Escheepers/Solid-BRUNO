using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IBookingRepository"/> against <see cref="AppDbContext"/>.
/// Booking has no soft-delete/query filter concept of its own (unlike Vehicle/Customer), so every
/// read here starts from the <c>Bookings</c> table directly with no <c>IgnoreQueryFilters()</c>
/// consideration for Booking itself. Mirrors <c>CustomerRepository</c>/<c>VehicleRepository</c>'s
/// exact doc-comment style and shape.
/// </summary>
public class BookingRepository(AppDbContext dbContext) : IBookingRepository
{
    public async Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AnyAsync(b => b.CustomerId == customerId, cancellationToken);
    }

    /// <summary>
    /// Joins each Booking to its referenced Vehicle/Customer row via <c>IgnoreQueryFilters()</c> on
    /// BOTH sides from day one (spec-4-1's Scope decision 4/AD-13) -- not deferred to a later story
    /// -- so a booking referencing an already-soft-deleted vehicle or an already-soft-deleted/
    /// anonymized customer still renders correctly in this list today, rather than silently dropping
    /// the row via EF Core's global query filter on the join. The total count is taken from the
    /// <c>Bookings</c> table alone (never the join): the FK constraints on VehicleId/CustomerId
    /// (<c>AppDbContext</c>, <c>DeleteBehavior.Restrict</c>) guarantee every Booking row always has a
    /// matching Vehicle/Customer row to join to, soft-deleted or not, so the inner join can never
    /// drop a row -- computing the count from the join instead would just be a slower, equivalent
    /// query. Ordered by <c>CreatedDate</c> then <c>Id</c> for a stable page boundary across
    /// requests, mirroring <c>VehicleRepository</c>/<c>CustomerRepository</c>'s exact ordering.
    /// </summary>
    public async Task<(IReadOnlyList<(Booking Booking, Vehicle Vehicle, Customer Customer)> Items, int TotalCount)>
        GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await dbContext.Bookings.CountAsync(cancellationToken);

        var rows = await (
                from booking in dbContext.Bookings
                join vehicle in dbContext.Vehicles.IgnoreQueryFilters() on booking.VehicleId equals vehicle.Id
                join customer in dbContext.Customers.IgnoreQueryFilters() on booking.CustomerId equals customer.Id
                orderby booking.CreatedDate, booking.Id
                select new { booking, vehicle, customer })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => (row.booking, row.vehicle, row.customer))
            .ToList();

        return (items, totalCount);
    }

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken)
    {
        await dbContext.Bookings.AddAsync(booking, cancellationToken);
    }

    /// <summary>
    /// Backs <see cref="IBookingRepository.GetNonCancelledForVehicleAsync"/>: every Booking row for
    /// <paramref name="vehicleId"/> whose <c>Status</c> is not <c>Cancelled</c>, no ordering guarantee
    /// needed since the caller (<c>CreateBookingCommandHandler</c>) only ever looks for the first
    /// overlap, not a stable page.
    /// </summary>
    public async Task<IReadOnlyList<Booking>> GetNonCancelledForVehicleAsync(
        Guid vehicleId, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .Where(b => b.VehicleId == vehicleId && b.Status != BookingStatus.Cancelled)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Backs <see cref="IBookingRepository.GetByIdAsync"/>: a direct, unfiltered lookup against the
    /// <c>Bookings</c> table -- no <c>IgnoreQueryFilters()</c> needed since Booking has no
    /// soft-delete/query-filter concept, unlike <c>VehicleRepository</c>/<c>CustomerRepository</c>'s
    /// own <c>GetByIdAsync</c>.
    /// </summary>
    public async Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }
}
