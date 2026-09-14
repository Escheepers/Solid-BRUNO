using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves the AddCustomersTable EF Core migration against a real, ephemeral Postgres container --
/// not manual inspection: the "Customers" table is created with the exact domain-model.md schema
/// (including the EmailHash shadow column), a second migrate is a no-op, and the unique constraint
/// on EmailHash is enforced at the database level. Mirrors <c>VehicleMigrationTests</c>'s exact
/// pattern.
/// </summary>
public class CustomerMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, new EphemeralDataProtectionProvider());
    }

    [Fact]
    public async Task Migrate_CreatesCustomersTable_WithExpectedSchema()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Customers'
            """;

        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        columns.Should().BeEquivalentTo(
            ["Id", "FirstName", "LastName", "Email", "PhoneNumber", "EmailHash", "CreatedDate", "IsDeleted", "IsAnonymized"]);
    }

    [Fact]
    public async Task Migrate_HasUniqueIndex_OnEmailHash()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'Customers' AND indexdef ILIKE '%EmailHash%'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var found = false;
        while (await reader.ReadAsync())
        {
            if (reader.GetString(0).Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
            }
        }

        found.Should().BeTrue("EmailHash must have a unique index/constraint at the database level");
    }

    [Fact]
    public async Task Migrate_IsNoOp_WhenAlreadyApplied()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var act = () => dbContext.Database.MigrateAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Customers_RejectsDuplicateNormalizedEmail_AtTheDatabaseLevel()
    {
        var sharedProtectionProvider = new EphemeralDataProtectionProvider();

        AppDbContext CreateContextSharingKeys()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            return new AppDbContext(options, sharedProtectionProvider);
        }

        await using var seedContext = CreateContextSharingKeys();
        await seedContext.Database.MigrateAsync();

        seedContext.Customers.Add(Customer.Create(
            "Jane", "Doe", "duplicate@example.com", "0821234567"));
        await seedContext.SaveChangesAsync();

        await using var duplicateContext = CreateContextSharingKeys();
        // Different case/whitespace -- still the same normalized email, so still a DB-level clash.
        duplicateContext.Customers.Add(Customer.Create(
            "John", "Smith", " Duplicate@Example.com ", "0827654321"));

        var act = () => duplicateContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
