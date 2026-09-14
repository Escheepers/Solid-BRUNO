using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves the AddBookingsTable EF Core migration against a real, ephemeral Postgres container --
/// not manual inspection: the "Bookings" table is created with the exact domain-model.md schema,
/// a second migrate is a no-op, both FK columns are indexed, and the FK constraints actually reject
/// an orphaned VehicleId/CustomerId reference (a real, not assumed, proof). Mirrors
/// <c>VehicleMigrationTests</c>/<c>CustomerMigrationTests</c>'s exact pattern.
/// </summary>
public class BookingMigrationTests : IAsyncLifetime
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

        // An ephemeral provider is fine here -- this class exercises the Booking/Vehicle/Customer
        // schema shape only, never asserts anything about PII encryption's actual ciphertext.
        return new AppDbContext(options, new EphemeralDataProtectionProvider());
    }

    [Fact]
    public async Task Migrate_CreatesBookingsTable_WithExpectedSchema()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Bookings'
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
            ["Id", "VehicleId", "CustomerId", "StartDate", "EndDate", "TotalPrice", "Status", "CreatedDate"]);
    }

    [Fact]
    public async Task Migrate_StartAndEndDate_AreMappedAsNativeDateColumns()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Bookings'
                AND column_name IN ('StartDate', 'EndDate')
            """;

        var dataTypesByColumn = new Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                dataTypesByColumn[reader.GetString(0)] = reader.GetString(1);
            }
        }

        dataTypesByColumn["StartDate"].Should().Be("date");
        dataTypesByColumn["EndDate"].Should().Be("date");
    }

    [Fact]
    public async Task Migrate_HasIndexes_OnBothVehicleIdAndCustomerId()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'Bookings'
            """;

        var indexDefs = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexDefs.Add(reader.GetString(0));
            }
        }

        indexDefs.Should().Contain(def => def.Contains("VehicleId", StringComparison.OrdinalIgnoreCase));
        indexDefs.Should().Contain(def => def.Contains("CustomerId", StringComparison.OrdinalIgnoreCase));
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
    public async Task Bookings_RejectsOrphanedVehicleId_AtTheDatabaseLevel()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var customer = Customer.Create("Jane", "Doe", "jane.orphan-vehicle@example.com", "0821234567");
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        var booking = Booking.Create(
            vehicleId: Guid.NewGuid(), // never persisted -- an orphaned reference
            customerId: customer.Id,
            startDate: new DateOnly(2026, 10, 1),
            endDate: new DateOnly(2026, 10, 5),
            totalPrice: 1500m);
        dbContext.Bookings.Add(booking);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "Postgres must reject a Booking row referencing a nonexistent VehicleId");
    }

    [Fact]
    public async Task Bookings_RejectsOrphanedCustomerId_AtTheDatabaseLevel()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var vehicle = Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);
        dbContext.Vehicles.Add(vehicle);
        await dbContext.SaveChangesAsync();

        var booking = Booking.Create(
            vehicleId: vehicle.Id,
            customerId: Guid.NewGuid(), // never persisted -- an orphaned reference
            startDate: new DateOnly(2026, 10, 1),
            endDate: new DateOnly(2026, 10, 5),
            totalPrice: 1500m);
        dbContext.Bookings.Add(booking);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "Postgres must reject a Booking row referencing a nonexistent CustomerId");
    }

    [Fact]
    public async Task Bookings_AcceptsAValidRow_ReferencingRealVehicleAndCustomer()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var vehicle = Vehicle.Create("CA654321", "Honda", "Civic", 2024, 400m);
        var customer = Customer.Create("John", "Smith", "john.valid-booking@example.com", "0827654321");
        dbContext.Vehicles.Add(vehicle);
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        var booking = Booking.Create(
            vehicleId: vehicle.Id,
            customerId: customer.Id,
            startDate: new DateOnly(2026, 10, 1),
            endDate: new DateOnly(2026, 10, 5),
            totalPrice: 1600m);
        dbContext.Bookings.Add(booking);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
