using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Seeding;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves <see cref="DatabaseSeeder"/> against a real, ephemeral Postgres container -- not a mock,
/// and not an in-memory provider, since both the PII value converter (AD-12) and the Booking overlap
/// exclusion constraint (a raw-SQL migration, spec-4-2) need a genuine Postgres engine to exercise.
/// Written test-first (TDD), mirroring <see cref="CustomerRepositoryTests"/>/
/// <see cref="CustomerMigrationTests"/>'s exact container-per-test-method setup. Covers spec-6-1's
/// three explicitly-named proofs: idempotency, genuine PII encryption at rest, and no overlap-
/// constraint violation across seeded Bookings.
/// </summary>
public class DatabaseSeederTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    private readonly IDataProtectionProvider _dataProtectionProvider = new EphemeralDataProtectionProvider();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        // An ephemeral provider is fine here -- every dbContext in a single test shares this one
        // instance's keys, so anything encrypted by one CreateDbContext() call decrypts correctly
        // via another, exactly like CustomerRepositoryTests' own precedent.
        return new AppDbContext(options, _dataProtectionProvider);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_PopulatesEveryInterestingState()
    {
        await using var seedContext = CreateDbContext();
        await new DatabaseSeeder(seedContext).SeedIfEmptyAsync(CancellationToken.None);

        await using var readContext = CreateDbContext();
        var vehicles = await readContext.Vehicles.IgnoreQueryFilters().ToListAsync();
        var customers = await readContext.Customers.IgnoreQueryFilters().ToListAsync();
        var bookings = await readContext.Bookings.ToListAsync();

        // spec-6-1's Code Map: ~15 Vehicles (1-2 soft-deleted), ~15 Customers (1 soft-deleted, 1
        // anonymized), ~25+ Bookings spanning every BookingStatus.
        vehicles.Count.Should().BeGreaterThanOrEqualTo(15);
        vehicles.Count(v => v.IsDeleted).Should().BeGreaterThanOrEqualTo(1);

        customers.Count.Should().BeGreaterThanOrEqualTo(15);
        customers.Count(c => c.IsDeleted).Should().BeGreaterThanOrEqualTo(1);
        customers.Count(c => c.IsAnonymized).Should().BeGreaterThanOrEqualTo(1);

        bookings.Count.Should().BeGreaterThanOrEqualTo(25);
        bookings.Should().Contain(b => b.Status == BookingStatus.Active);
        bookings.Should().Contain(b => b.Status == BookingStatus.Completed);
        bookings.Should().Contain(b => b.Status == BookingStatus.Cancelled);
    }

    /// <summary>
    /// spec-6-1's Scope decision 2: idempotent via an empty-check, not a re-runnable/upsert design.
    /// A second <see cref="DatabaseSeeder.SeedIfEmptyAsync"/> call against an already-seeded database
    /// must be a complete no-op -- identical row counts, not doubled ones.
    /// </summary>
    [Fact]
    public async Task SeedIfEmptyAsync_CalledTwice_DoesNotDuplicate()
    {
        await using (var firstPassContext = CreateDbContext())
        {
            await new DatabaseSeeder(firstPassContext).SeedIfEmptyAsync(CancellationToken.None);
        }

        int vehicleCountAfterFirstPass, customerCountAfterFirstPass, bookingCountAfterFirstPass;
        await using (var countContext = CreateDbContext())
        {
            vehicleCountAfterFirstPass = await countContext.Vehicles.IgnoreQueryFilters().CountAsync();
            customerCountAfterFirstPass = await countContext.Customers.IgnoreQueryFilters().CountAsync();
            bookingCountAfterFirstPass = await countContext.Bookings.CountAsync();
        }

        await using (var secondPassContext = CreateDbContext())
        {
            await new DatabaseSeeder(secondPassContext).SeedIfEmptyAsync(CancellationToken.None);
        }

        await using var finalContext = CreateDbContext();
        (await finalContext.Vehicles.IgnoreQueryFilters().CountAsync()).Should().Be(vehicleCountAfterFirstPass);
        (await finalContext.Customers.IgnoreQueryFilters().CountAsync()).Should().Be(customerCountAfterFirstPass);
        (await finalContext.Bookings.CountAsync()).Should().Be(bookingCountAfterFirstPass);
    }

    /// <summary>
    /// Mirrors spec-3-1's own raw-column ciphertext-proof technique (see
    /// <c>CustomersEndpointTests.Post_ValidCommand_RawDatabaseRow_HasEncryptedEmailAndPhoneNumber_NotPlaintext</c>):
    /// reads a seeded Customer's "Email" column directly via <see cref="NpgsqlConnection"/> -- bypassing
    /// EF Core and its value converter entirely -- and asserts it is neither equal to, nor contains as
    /// a substring, the real plaintext (obtained here via EF's own decrypting read, since that is
    /// exactly the value Bogus generated and the seeder submitted). Proves the seeder's writes go
    /// through the same AD-12 value converter as any other write, never bypassing it via raw SQL.
    /// </summary>
    [Fact]
    public async Task SeedIfEmptyAsync_EncryptsCustomerEmail_AtRest()
    {
        await using var seedContext = CreateDbContext();
        await new DatabaseSeeder(seedContext).SeedIfEmptyAsync(CancellationToken.None);

        await using var readContext = CreateDbContext();
        var customer = await readContext.Customers.IgnoreQueryFilters().FirstAsync();
        var plaintextEmail = customer.Email;

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Email"
            FROM "Customers"
            WHERE "Id" = @id
            """;
        command.Parameters.AddWithValue("id", customer.Id);

        var rawEmail = (string)(await command.ExecuteScalarAsync())!;

        rawEmail.Should().NotBe(plaintextEmail);
        rawEmail.Should().NotContain(plaintextEmail);
    }

    /// <summary>
    /// spec-6-1's Scope decision 4: per-vehicle booking date ranges are generated sequentially so none
    /// violate the overlap constraint. A violation would already have thrown a
    /// <c>DbUpdateException</c> (Postgres SQLSTATE 23P01) on <see cref="DatabaseSeeder.SeedIfEmptyAsync"/>'s
    /// own <c>SaveChangesAsync</c> call, against the real <c>EX_Bookings_VehicleId_DateRange_NoOverlap</c>
    /// exclusion constraint (spec-4-2) -- so simply completing without throwing is this test's primary
    /// proof. The explicit re-check below over the read-back rows is a second, independent proof using
    /// the exact same half-open <see cref="DateRange"/> semantics the real application-layer check uses,
    /// mirroring the constraint's own <c>WHERE ("Status" &lt;&gt; 'Cancelled')</c> clause.
    /// </summary>
    [Fact]
    public async Task SeedIfEmptyAsync_NeverProducesOverlappingNonCancelledBookings_ForTheSameVehicle()
    {
        await using var seedContext = CreateDbContext();

        var act = () => new DatabaseSeeder(seedContext).SeedIfEmptyAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using var readContext = CreateDbContext();
        var nonCancelledBookings = await readContext.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .ToListAsync();

        foreach (var vehicleGroup in nonCancelledBookings.GroupBy(b => b.VehicleId))
        {
            var ranges = vehicleGroup.Select(b => new DateRange(b.StartDate, b.EndDate)).ToList();

            for (var i = 0; i < ranges.Count; i++)
            {
                for (var j = i + 1; j < ranges.Count; j++)
                {
                    ranges[i].Overlaps(ranges[j]).Should().BeFalse(
                        $"non-Cancelled Bookings for Vehicle {vehicleGroup.Key} must never overlap.");
                }
            }
        }
    }
}
