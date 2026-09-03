using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IVehicleRepository"/> against <see cref="AppDbContext"/>.
/// Relies entirely on the Vehicle entity's global query filter (AD-13) to exclude soft-deleted
/// rows -- never filters on <c>IsDeleted</c> explicitly here. Search matches
/// Make/Model/RegistrationNumber via <see cref="EF.Functions"/>' <c>ILike</c> (Postgres
/// case-insensitive <c>ILIKE</c>), applied only when the <c>search</c> parameter is non-null and
/// non-whitespace. Ordered by <c>CreatedDate</c> then <c>Id</c> for a stable page boundary across
/// requests.
/// </summary>
public class VehicleRepository(AppDbContext dbContext) : IVehicleRepository
{
    public async Task<(IReadOnlyList<Vehicle> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Vehicles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(v =>
                EF.Functions.ILike(v.Make, $"%{search}%") ||
                EF.Functions.ILike(v.Model, $"%{search}%") ||
                EF.Functions.ILike(v.RegistrationNumber, $"%{search}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(v => v.CreatedDate)
            .ThenBy(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
