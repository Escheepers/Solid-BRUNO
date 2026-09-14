using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICustomerRepository"/> against <see cref="AppDbContext"/>.
/// Relies entirely on the Customer entity's global query filter (AD-13) to exclude soft-deleted
/// rows -- never filters on <c>IsDeleted</c> explicitly here (mirrors <c>VehicleRepository</c>).
/// Search matches FirstName/LastName ONLY via <see cref="EF.Functions"/>' <c>ILike</c> -- never
/// Email/PhoneNumber, since those are encrypted at rest and pattern-matching ciphertext would be
/// meaningless (spec-3-1's Scope decision 2). Ordered by <c>CreatedDate</c> then <c>Id</c> for a
/// stable page boundary across requests.
/// </summary>
public class CustomerRepository(AppDbContext dbContext) : ICustomerRepository
{
    public async Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Customers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                EF.Functions.ILike(c.FirstName, $"%{search}%") ||
                EF.Functions.ILike(c.LastName, $"%{search}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(c => c.CreatedDate)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <summary>
    /// Computes the hash via the same <see cref="EmailHasher.Compute"/> used by
    /// <c>AppDbContext.SaveChangesAsync</c>'s override (DRY -- the ONE place this normalization/
    /// hashing logic exists), then compares it against the stored EmailHash shadow property.
    /// Deliberately calls <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
    /// -- the DB's unique index on EmailHash (see <c>AppDbContext</c>) is itself unfiltered, so this
    /// pre-check must match that same unfiltered scope. Mirrors
    /// <c>VehicleRepository.ExistsByRegistrationNumberAsync</c>'s exact reasoning.
    /// </summary>
    public async Task<bool> ExistsByEmailAsync(string email, Guid? excludingId, CancellationToken cancellationToken)
    {
        var hash = EmailHasher.Compute(email);

        return await dbContext.Customers
            .IgnoreQueryFilters()
            .AnyAsync(
                c => EF.Property<string>(c, "EmailHash") == hash && (excludingId == null || c.Id != excludingId),
                cancellationToken);
    }

    /// <summary>
    /// A plain, filtered lookup -- deliberately does NOT call <c>IgnoreQueryFilters()</c> (unlike
    /// <see cref="ExistsByEmailAsync"/> above). Mirrors <c>VehicleRepository.GetByIdAsync</c> exactly.
    /// </summary>
    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Customers
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        await dbContext.Customers.AddAsync(customer, cancellationToken);
    }

    /// <summary>
    /// Queues a genuine, permanent removal -- the first real hard delete anywhere in this repository
    /// layer (Story 3.3). Deliberately synchronous under the hood (<c>DbSet.Remove</c> has no async
    /// overload, mirroring <c>AddAsync</c>'s own use of an actually-async EF Core API where one
    /// exists) -- still returns a <see cref="Task"/> to match this interface's shape.
    /// </summary>
    public Task RemoveAsync(Customer customer, CancellationToken cancellationToken)
    {
        dbContext.Customers.Remove(customer);
        return Task.CompletedTask;
    }
}
