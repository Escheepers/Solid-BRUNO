using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves <see cref="BookingRepository.ExistsActiveForVehicleAsync"/> against a real, ephemeral
/// Postgres container -- not a mock (spec-vehicle-deactivate-blocked-by-active-bookings), mirroring
/// <see cref="CustomerRepositoryTests"/>'s exact shape/pattern. Written test-first (before the method
/// existed on <see cref="BookingRepository"/>).
/// </summary>
public class BookingRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private readonly IDataProtectionProvider _dataProtectionProvider = new EphemeralDataProtectionProvider();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, _dataProtectionProvider);
    }

    private static Vehicle CreateValidVehicle(string registrationNumber = "CA123456") =>
        Vehicle.Create(registrationNumber, "Toyota", "Corolla", 2023, 350m);

    private static Customer CreateValidCustomer(string email = "jane.doe@example.com") =>
        Customer.Create("Jane", "Doe", email, "0821234567");

    private static Booking CreateBooking(
        Guid vehicleId,
        Guid customerId,
        DateOnly? startDate = null,
        DateOnly? endDate = null) =>
        Booking.Create(
            vehicleId,
            customerId,
            startDate ?? new DateOnly(2026, 10, 1),
            endDate ?? new DateOnly(2026, 10, 5),
            1400m);

    [Fact]
    public async Task ExistsActiveForVehicleAsync_VehicleHasActiveBooking_ReturnsTrue()
    {
        var vehicle = CreateValidVehicle();
        var customer = CreateValidCustomer();
        var booking = CreateBooking(vehicle.Id, customer.Id);

        await using var seedContext = CreateDbContext();
        seedContext.Vehicles.Add(vehicle);
        seedContext.Customers.Add(customer);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new BookingRepository(dbContext);

        var exists = await repository.ExistsActiveForVehicleAsync(vehicle.Id, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsActiveForVehicleAsync_VehicleHasOnlyCancelledBooking_ReturnsFalse()
    {
        var vehicle = CreateValidVehicle();
        var customer = CreateValidCustomer();
        // A future EndDate is required for Cancel() to succeed (a past-EndDate booking is already
        // ineligible per Booking.Cancel's own guard).
        var booking = CreateBooking(vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5));

        await using var seedContext = CreateDbContext();
        seedContext.Vehicles.Add(vehicle);
        seedContext.Customers.Add(customer);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        booking.Cancel(new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new BookingRepository(dbContext);

        var exists = await repository.ExistsActiveForVehicleAsync(vehicle.Id, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsActiveForVehicleAsync_VehicleHasOnlyCompletedBooking_ReturnsFalse()
    {
        var vehicle = CreateValidVehicle();
        var customer = CreateValidCustomer();
        var booking = CreateBooking(vehicle.Id, customer.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5));

        await using var seedContext = CreateDbContext();
        seedContext.Vehicles.Add(vehicle);
        seedContext.Customers.Add(customer);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        // Booking has no public Complete() reachable from this project's own test-only "now" without
        // a sweep -- flip the Status column directly, exactly as BookingsEndpointTests's own
        // SeedBookingWithStatusAsync does, to reach a state a real sweep would eventually produce.
        await seedContext.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Bookings" SET "Status" = {nameof(BookingStatus.Completed)} WHERE "Id" = {booking.Id}""");

        await using var dbContext = CreateDbContext();
        var repository = new BookingRepository(dbContext);

        var exists = await repository.ExistsActiveForVehicleAsync(vehicle.Id, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsActiveForVehicleAsync_VehicleHasNoBookings_ReturnsFalse()
    {
        var vehicle = CreateValidVehicle();

        await using var seedContext = CreateDbContext();
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new BookingRepository(dbContext);

        var exists = await repository.ExistsActiveForVehicleAsync(vehicle.Id, CancellationToken.None);

        exists.Should().BeFalse();
    }

    /// <summary>Minimal fixed-clock test double -- mirrors <c>BookingCompletionSweepServiceTests</c>'s own.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
