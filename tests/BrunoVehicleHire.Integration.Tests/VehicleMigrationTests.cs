using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves the EF Core migration against a real, ephemeral Postgres container -- not manual
/// inspection: the "Vehicles" table is created with the exact domain-model.md schema, a second
/// migrate is a no-op, and the unique constraint on RegistrationNumber is enforced at the
/// database level (matching the I/O matrix in the story spec).
/// </summary>
public class VehicleMigrationTests : IAsyncLifetime
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

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Migrate_CreatesVehiclesTable_WithExpectedSchema()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Vehicles'
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
            ["Id", "RegistrationNumber", "Make", "Model", "Year", "DailyRate", "IsDeleted", "CreatedDate"]);
    }

    [Fact]
    public async Task Migrate_HasUniqueIndex_OnRegistrationNumber()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'Vehicles' AND indexdef ILIKE '%RegistrationNumber%'
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

        found.Should().BeTrue("RegistrationNumber must have a unique index/constraint at the database level");
    }

    [Fact]
    public async Task Migrate_IsNoOp_WhenAlreadyApplied()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        // Second startup against an already-migrated database must succeed unchanged.
        var act = () => dbContext.Database.MigrateAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Vehicles_RejectsDuplicateRegistrationNumber_AtTheDatabaseLevel()
    {
        await using var seedContext = CreateDbContext();
        await seedContext.Database.MigrateAsync();

        seedContext.Vehicles.Add(Vehicle.Create(
            registrationNumber: "CA123456",
            make: "Toyota",
            model: "Corolla",
            year: 2023,
            dailyRate: 350m));
        await seedContext.SaveChangesAsync();

        await using var duplicateContext = CreateDbContext();
        duplicateContext.Vehicles.Add(Vehicle.Create(
            registrationNumber: "CA123456",
            make: "Honda",
            model: "Civic",
            year: 2024,
            dailyRate: 400m));

        var act = () => duplicateContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
