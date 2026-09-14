using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IBookingRepository"/> against <see cref="AppDbContext"/>.
/// Booking has no soft-delete/query filter concept (unlike Vehicle/Customer), so this reads the
/// <c>Bookings</c> table directly with no <c>IgnoreQueryFilters()</c> consideration needed. Mirrors
/// <c>CustomerRepository</c>/<c>VehicleRepository</c>'s exact doc-comment style and shape.
/// </summary>
public class BookingRepository(AppDbContext dbContext) : IBookingRepository
{
    public async Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AnyAsync(b => b.CustomerId == customerId, cancellationToken);
    }
}
