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

    /// <summary>
    /// Proves spec-4-2's AddBookingOverlapExclusionConstraint migration actually created a Postgres
    /// exclusion constraint (<c>pg_constraint.contype = 'x'</c>) on "Bookings" -- not just that some
    /// index/constraint with a plausible name exists.
    /// </summary>
    [Fact]
    public async Task Migrate_CreatesExclusionConstraint_OnBookings()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT conname, contype
            FROM pg_constraint
            WHERE conrelid = '"Bookings"'::regclass AND contype = 'x'
            """;

        var exclusionConstraints = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                exclusionConstraints.Add(reader.GetString(0));
            }
        }

        exclusionConstraints.Should().ContainSingle(
            "exactly one EXCLUDE USING GIST constraint must exist on \"Bookings\"");
    }

    [Fact]
    public async Task Bookings_RejectsOverlappingActiveBooking_ForSameVehicle_AtTheDatabaseLevel()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var vehicle = Vehicle.Create("CA111222", "Toyota", "Hilux", 2023, 500m);
        var customerA = Customer.Create("Jane", "Doe", "jane.db-overlap-a@example.com", "0821234567");
        var customerB = Customer.Create("John", "Smith", "john.db-overlap-b@example.com", "0827654321");
        dbContext.Vehicles.Add(vehicle);
        dbContext.Customers.AddRange(customerA, customerB);
        await dbContext.SaveChangesAsync();

        dbContext.Bookings.Add(Booking.Create(
            vehicle.Id, customerA.Id, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 4), 1000m));
        await dbContext.SaveChangesAsync();

        // A second, overlapping booking for the SAME vehicle, inserted directly against the
        // DbContext -- CreateBookingCommandHandler's application-level pre-check is never involved
        // here, so this proves the database constraint itself, independent of that check.
        dbContext.Bookings.Add(Booking.Create(
            vehicle.Id, customerB.Id, new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 5), 1000m));

        var act = () => dbContext.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        (exception.Which.InnerException as PostgresException)?.SqlState.Should().Be("23P01");
    }

    [Fact]
    public async Task Bookings_AllowsSameDayTurnover_ForSameVehicle_AtTheDatabaseLevel()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var vehicle = Vehicle.Create("CA333444", "Toyota", "Fortuner", 2023, 600m);
        var customerA = Customer.Create("Jane", "Doe", "jane.db-turnover-a@example.com", "0821234567");
        var customerB = Customer.Create("John", "Smith", "john.db-turnover-b@example.com", "0827654321");
        dbContext.Vehicles.Add(vehicle);
        dbContext.Customers.AddRange(customerA, customerB);
        await dbContext.SaveChangesAsync();

        dbContext.Bookings.Add(Booking.Create(
            vehicle.Id, customerA.Id, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 4), 1200m));
        await dbContext.SaveChangesAsync();

        // Starts exactly on the first booking's EndDate -- a touching endpoint, not an overlap
        // (half-open interval semantics), so the exclusion constraint must allow this too.
        dbContext.Bookings.Add(Booking.Create(
            vehicle.Id, customerB.Id, new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 6), 1200m));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Bookings_AllowsOverlapWithACancelledBooking_ForSameVehicle_AtTheDatabaseLevel()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var vehicle = Vehicle.Create("CA555666", "Toyota", "Land Cruiser", 2023, 900m);
        var customerA = Customer.Create("Jane", "Doe", "jane.db-cancelled-a@example.com", "0821234567");
        var customerB = Customer.Create("John", "Smith", "john.db-cancelled-b@example.com", "0827654321");
        dbContext.Vehicles.Add(vehicle);
        dbContext.Customers.AddRange(customerA, customerB);
        await dbContext.SaveChangesAsync();

        var cancelledBooking = Booking.Create(
            vehicle.Id, customerA.Id, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 4), 1000m);
        dbContext.Bookings.Add(cancelledBooking);
        await dbContext.SaveChangesAsync();

        // Booking has no public Cancel() transition yet (out of this story's scope) -- flip the
        // Status column directly via raw SQL, exactly as a future Cancel feature eventually would,
        // to prove the constraint's WHERE ("Status" <> 'Cancelled') clause, not merely that this
        // column exists.
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """UPDATE "Bookings" SET "Status" = 'Cancelled' WHERE "Id" = @id""";
            command.Parameters.AddWithValue("id", cancelledBooking.Id);
            await command.ExecuteNonQueryAsync();
        }

        // An overlapping range for the same vehicle -- allowed because the only conflicting row is
        // now Cancelled.
        dbContext.Bookings.Add(Booking.Create(
            vehicle.Id, customerB.Id, new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 5), 1000m));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// spec-4-2's own required concurrency proof: two overlapping inserts for the same vehicle, each
    /// on its own <see cref="AppDbContext"/>/connection, issued concurrently via
    /// <see cref="Task.WhenAll(Task[])"/> and racing against the real, ephemeral Postgres container --
    /// never going through <c>CreateBookingCommandHandler</c>'s application-level pre-check, so this
    /// exercises only AD-7's layer 2 (the database constraint). Postgres serializes concurrent
    /// exclusion-constraint checks against the same conflicting range (the second inserter waits for
    /// the first's transaction to resolve before either succeeding or raising the violation), so
    /// exactly one of the two must succeed and the other must fail with SQLSTATE 23P01, regardless of
    /// which one the scheduler happens to run first.
    /// </summary>
    [Fact]
    public async Task ConcurrentOverlappingInserts_ForSameVehicle_ExactlyOneSucceeds_TheOtherThrowsExclusionViolation()
    {
        await using var seedContext = CreateDbContext();
        await seedContext.Database.MigrateAsync();

        var vehicle = Vehicle.Create("CA777888", "Toyota", "Prado", 2023, 750m);
        var customerA = Customer.Create("Jane", "Doe", "jane.race-a@example.com", "0821234567");
        var customerB = Customer.Create("John", "Smith", "john.race-b@example.com", "0827654321");
        seedContext.Vehicles.Add(vehicle);
        seedContext.Customers.AddRange(customerA, customerB);
        await seedContext.SaveChangesAsync();

        await using var contextA = CreateDbContext();
        await using var contextB = CreateDbContext();

        // Genuinely overlapping ranges (3-7 Nov shares 3-5 Nov with 1-5 Nov) for the SAME vehicle.
        contextA.Bookings.Add(Booking.Create(
            vehicle.Id, customerA.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), 1500m));
        contextB.Bookings.Add(Booking.Create(
            vehicle.Id, customerB.Id, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 7), 1500m));

        static async Task<(bool Succeeded, DbUpdateException? Failure)> TrySaveAsync(AppDbContext context)
        {
            try
            {
                await context.SaveChangesAsync();
                return (true, null);
            }
            catch (DbUpdateException ex)
            {
                return (false, ex);
            }
        }

        var taskA = TrySaveAsync(contextA);
        var taskB = TrySaveAsync(contextB);

        var outcomes = await Task.WhenAll(taskA, taskB);

        outcomes.Count(outcome => outcome.Succeeded).Should().Be(
            1, "exactly one of the two concurrently-racing overlapping inserts must win");

        var rejected = outcomes.Single(outcome => !outcome.Succeeded);
        rejected.Failure.Should().NotBeNull();
        (rejected.Failure!.InnerException as PostgresException)?.SqlState.Should().Be(
            "23P01", "the loser must fail specifically via the exclusion constraint, not some other error");
    }
}
