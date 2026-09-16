namespace BrunoVehicleHire.Infrastructure.Seeding;

/// <summary>
/// Populates a fresh database with synthetic-but-realistic demo data so an evaluator's
/// `docker-compose up` + `dotnet run` reaches an explorable, fully-populated system with zero manual
/// setup (spec-6-1, CAP-10). Deliberately a single-purpose interface (YAGNI) -- nothing beyond
/// <see cref="DatabaseSeeder"/> implements it, but it exists so <c>Program.cs</c> resolves it from DI
/// rather than depending on the concrete class directly, and so a test double could stand in without
/// touching a real database if ever needed.
/// </summary>
public interface ISeeder
{
    /// <summary>
    /// Seeds Vehicles, Customers, and Bookings if -- and only if -- the database is currently empty
    /// of Vehicles (spec-6-1's Scope decision 2: idempotent via an empty-check, not a
    /// re-runnable/upsert design). A repeated call against an already-seeded database is a no-op,
    /// never a duplicate or an error.
    /// </summary>
    Task SeedIfEmptyAsync(CancellationToken cancellationToken);
}
