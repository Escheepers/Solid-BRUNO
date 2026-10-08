using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Helpers;
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
    /// Backs <see cref="IBookingRepository.ExistsActiveForVehicleAsync"/>: true if any Booking row for
    /// <paramref name="vehicleId"/> is currently <see cref="BookingStatus.Active"/> -- no query filter
    /// concerns, mirroring every other method here (Booking has none of its own).
    /// </summary>
    public async Task<bool> ExistsActiveForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AnyAsync(b => b.VehicleId == vehicleId && b.Status == BookingStatus.Active, cancellationToken);
    }

    /// <summary>
    /// Backs <see cref="IBookingRepository.ExistsActiveForCustomerAsync"/>: true if any Booking row for
    /// <paramref name="customerId"/> is currently <see cref="BookingStatus.Active"/> -- mirrors
    /// <see cref="ExistsActiveForVehicleAsync"/> exactly.
    /// </summary>
    public async Task<bool> ExistsActiveForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AnyAsync(b => b.CustomerId == customerId && b.Status == BookingStatus.Active, cancellationToken);
    }

    /// <summary>
    /// Joins each Booking to its referenced Vehicle/Customer row via <c>IgnoreQueryFilters()</c> on
    /// BOTH sides from day one (spec-4-1's Scope decision 4/AD-13) -- not deferred to a later story
    /// -- so a booking referencing an already-soft-deleted vehicle or an already-soft-deleted/
    /// anonymized customer still renders correctly in this list today, rather than silently dropping
    /// the row via EF Core's global query filter on the join. <paramref name="vehicleId"/> is
    /// <c>null</c> for the unfiltered Bookings list, or set for Vehicle Detail's booking-history
    /// section (spec-4-5's Scope decision 1) -- filtered on the un-joined <c>Bookings</c> table before
    /// the join, since it needs no Vehicle/Customer field. <paramref name="search"/>
    /// (spec-bookings-search) case-insensitively substring-matches the joined Vehicle's
    /// Make/Model/RegistrationNumber or Customer's FirstName/LastName -- EVERY space-separated word of
    /// <paramref name="search"/> must match at least one of those five fields (spec-search-multi-word;
    /// OR'd across all five fields per word, AND'd across words, via
    /// <see cref="EF.Functions"/>' <c>ILike</c>, mirroring <c>VehicleRepository.GetPagedAsync</c>'s
    /// exact pattern), applied only when non-null/non-whitespace. Because <paramref name="search"/>
    /// needs the join's Vehicle/Customer columns, the total count is now taken from the same joined
    /// (and, when set, search-filtered) query the paged rows are sliced from -- one query shape for
    /// both, rather than the unfiltered-count-from-Bookings-alone shortcut this method used before
    /// <paramref name="search"/> existed. Ordered newest-first (<c>CreatedDate</c> then <c>Id</c>,
    /// both descending) so a just-created booking is always on page 1, with a stable page boundary
    /// across requests, mirroring <c>VehicleRepository</c>/<c>CustomerRepository</c>'s exact ordering.
    /// </summary>
    public async Task<(IReadOnlyList<(Booking Booking, Vehicle Vehicle, Customer Customer)> Items, int TotalCount)>
        GetPagedAsync(int page, int pageSize, Guid? vehicleId, string? search, CancellationToken cancellationToken)
    {
        var bookingsQuery = dbContext.Bookings.AsQueryable();
        if (vehicleId is not null)
        {
            bookingsQuery = bookingsQuery.Where(b => b.VehicleId == vehicleId);
        }

        var joinedQuery =
            from booking in bookingsQuery
            join vehicle in dbContext.Vehicles.IgnoreQueryFilters() on booking.VehicleId equals vehicle.Id
            join customer in dbContext.Customers.IgnoreQueryFilters() on booking.CustomerId equals customer.Id
            select new { booking, vehicle, customer };

        // Every space-separated word must match one of the five fields (see ContainsPatterns), so a
        // search can mix vehicle and customer words: "Toyota Ericka", "Ericka U".
        foreach (var pattern in LikePatternEscaper.ContainsPatterns(search))
        {
            joinedQuery = joinedQuery.Where(row =>
                EF.Functions.ILike(row.vehicle.Make, pattern, LikePatternEscaper.EscapeCharacter) ||
                EF.Functions.ILike(row.vehicle.Model, pattern, LikePatternEscaper.EscapeCharacter) ||
                EF.Functions.ILike(row.vehicle.RegistrationNumber, pattern, LikePatternEscaper.EscapeCharacter) ||
                EF.Functions.ILike(row.customer.FirstName, pattern, LikePatternEscaper.EscapeCharacter) ||
                EF.Functions.ILike(row.customer.LastName, pattern, LikePatternEscaper.EscapeCharacter));
        }

        var totalCount = await joinedQuery.CountAsync(cancellationToken);

        var rows = await joinedQuery
            .OrderByDescending(row => row.booking.CreatedDate)
            .ThenByDescending(row => row.booking.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => (row.booking, row.vehicle, row.customer))
            .ToList();

        return (items, totalCount);
    }

    /// <summary>
    /// Backs <see cref="IBookingRepository.GetByIdWithVehicleAndCustomerAsync"/>: the same dual-
    /// <c>IgnoreQueryFilters()</c> join <see cref="GetPagedAsync"/> implements, narrowed to a single
    /// <paramref name="id"/> instead of a page (spec-4-5's Scope decision 2) -- so a booking
    /// referencing an already-soft-deleted vehicle or an already-soft-deleted/anonymized customer
    /// still resolves correctly for the detail view, exactly as it already does in the list.
    /// </summary>
    public async Task<(Booking Booking, Vehicle Vehicle, Customer Customer)?> GetByIdWithVehicleAndCustomerAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var row = await (
                from booking in dbContext.Bookings
                join vehicle in dbContext.Vehicles.IgnoreQueryFilters() on booking.VehicleId equals vehicle.Id
                join customer in dbContext.Customers.IgnoreQueryFilters() on booking.CustomerId equals customer.Id
                where booking.Id == id
                select new { booking, vehicle, customer })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : (row.booking, row.vehicle, row.customer);
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

    /// <summary>
    /// Backs <see cref="IBookingRepository.GetActivePastEndDateAsync"/>: every <c>Active</c> Booking
    /// whose <c>EndDate</c> is on or before <paramref name="asOf"/> -- no ordering guarantee needed,
    /// mirroring <see cref="GetNonCancelledForVehicleAsync"/>'s own rationale (the caller dispatches
    /// every row in the result, not just the first).
    /// </summary>
    public async Task<IReadOnlyList<Booking>> GetActivePastEndDateAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .Where(b => b.Status == BookingStatus.Active && b.EndDate <= asOf)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Backs <see cref="IBookingRepository.GetForCustomerWithVehicleAsync"/>: every Booking for
    /// <paramref name="customerId"/> joined to its referenced Vehicle row via
    /// <c>IgnoreQueryFilters()</c> (spec-5-1's Boundaries/AD-13), mirroring
    /// <see cref="GetPagedAsync"/>/<see cref="GetByIdWithVehicleAndCustomerAsync"/>'s exact join
    /// pattern minus the redundant Customer join (the caller already has that one row).
    /// </summary>
    public async Task<IReadOnlyList<(Booking Booking, Vehicle Vehicle)>> GetForCustomerWithVehicleAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var rows = await (
                from booking in dbContext.Bookings
                join vehicle in dbContext.Vehicles.IgnoreQueryFilters() on booking.VehicleId equals vehicle.Id
                where booking.CustomerId == customerId
                orderby booking.CreatedDate, booking.Id
                select new { booking, vehicle })
            .ToListAsync(cancellationToken);

        return rows.Select(row => (row.booking, row.vehicle)).ToList();
    }
}
