using BrunoVehicleHire.Application.Vehicles;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Helpers;
using BrunoVehicleHire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IVehicleRepository"/> against <see cref="AppDbContext"/>.
/// Relies entirely on the Vehicle entity's global query filter (AD-13) to exclude soft-deleted
/// rows -- never filters on <c>IsDeleted</c> explicitly here. Search matches
/// Make/Model/RegistrationNumber via <see cref="EF.Functions"/>' <c>ILike</c> (Postgres
/// case-insensitive <c>ILIKE</c>), applied only when the <c>search</c> parameter is non-null and
/// non-whitespace. Ordered newest-first (<c>CreatedDate</c> then <c>Id</c>, both descending) so a
/// just-created vehicle is always on page 1, with a stable page boundary across requests.
/// </summary>
public class VehicleRepository(AppDbContext dbContext) : IVehicleRepository
{
    public async Task<(IReadOnlyList<Vehicle> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = includeInactive
            ? dbContext.Vehicles.IgnoreQueryFilters()
            : dbContext.Vehicles.AsQueryable();

        // Every space-separated word must match Make, Model or RegistrationNumber (see ContainsPatterns).
        foreach (var pattern in LikePatternEscaper.ContainsPatterns(search))
        {
            query = query.Where(v =>
                EF.Functions.ILike(v.Make, pattern, LikePatternEscaper.EscapeCharacter) ||
                EF.Functions.ILike(v.Model, pattern, LikePatternEscaper.EscapeCharacter) ||
                EF.Functions.ILike(v.RegistrationNumber, pattern, LikePatternEscaper.EscapeCharacter));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(v => v.CreatedDate)
            .ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <summary>
    /// Deliberately calls <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
    /// -- the DB's unique index on RegistrationNumber (see <c>AppDbContext</c>) is itself unfiltered,
    /// so this pre-check must match that same unfiltered scope. Every other read in this repository
    /// relies on the global soft-delete query filter (AD-13); this is the one deliberate exception.
    /// </summary>
    public async Task<bool> ExistsByRegistrationNumberAsync(
        string registrationNumber, Guid? excludingId, CancellationToken cancellationToken)
    {
        return await dbContext.Vehicles
            .IgnoreQueryFilters()
            .AnyAsync(
                v => v.RegistrationNumber == registrationNumber && (excludingId == null || v.Id != excludingId),
                cancellationToken);
    }

    /// <summary>
    /// A plain, filtered lookup -- deliberately does NOT call <c>IgnoreQueryFilters()</c> (unlike
    /// <see cref="ExistsByRegistrationNumberAsync"/> above). See <c>IVehicleRepository</c>'s doc
    /// comment and spec-2-2's Design Notes for why the two methods differ.
    /// </summary>
    public async Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Vehicles
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    /// <summary>
    /// Deliberately calls <c>IgnoreQueryFilters()</c> -- mirrors
    /// <see cref="ExistsByRegistrationNumberAsync"/>'s reasoning. Restore's whole point is finding a
    /// currently-soft-deleted row, which <see cref="GetByIdAsync"/>'s filtered query would
    /// incorrectly report as not found.
    /// </summary>
    public async Task<Vehicle?> GetByIdIncludingSoftDeletedAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Vehicles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        await dbContext.Vehicles.AddAsync(vehicle, cancellationToken);
    }
}
