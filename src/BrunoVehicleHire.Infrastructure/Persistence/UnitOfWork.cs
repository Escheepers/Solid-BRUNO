using BrunoVehicleHire.Application.Common;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// Thin wrapper over <see cref="AppDbContext.SaveChangesAsync(CancellationToken)"/> -- the sole
/// commit point for every command handler (AD-5). Repositories only ever queue changes on the
/// tracked <see cref="AppDbContext"/>; this is the one place those changes are actually persisted.
/// </summary>
public class UnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
