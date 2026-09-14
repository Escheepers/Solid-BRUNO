using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.BackgroundServices;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves <see cref="BookingCompletionSweepService.RunSweepAsync"/> against a real, ephemeral
/// Postgres container -- never through <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>'s
/// own <c>ExecuteAsync</c> timer loop, and never waiting on a real interval (spec-4-4's own AC
/// requirement). Each test builds its own small, real DI container (real <see cref="IBookingRepository"/>/
/// <see cref="IUnitOfWork"/>/MediatR pipeline, real <see cref="CompleteBookingCommandHandler"/>) rather
/// than <c>WebApplicationFactory&lt;Program&gt;</c> -- booting the full Api host would also start its
/// own production <see cref="BookingCompletionSweepService"/> hosted service on the real
/// <see cref="TimeProvider.System"/> clock, racing this test's own fixed-clock sweep against the same
/// Postgres data. Building the container directly here keeps every sweep pass in this file entirely
/// test-controlled.
/// </summary>
public class BookingCompletionSweepServiceTests : IAsyncLifetime
{
    /// <summary>Fixes "today" so a booking ending on or before 2026-09-10 has already ended.</summary>
    private static readonly FixedTimeProvider TodayAfterBookings =
        new(new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero));

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

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

        return new AppDbContext(options, new EphemeralDataProtectionProvider());
    }

    /// <summary>
    /// A small, real DI container wiring exactly what <see cref="BookingCompletionSweepService.RunSweepAsync"/>
    /// needs: a fresh <see cref="AppDbContext"/> per scope, the real <see cref="BookingRepository"/>/
    /// <see cref="UnitOfWork"/>, and MediatR's real pipeline (so <see cref="CompleteBookingCommandHandler"/>
    /// actually runs). <paramref name="configureExtra"/> lets the mid-sweep-failure test inject one
    /// additional <see cref="IPipelineBehavior{TRequest,TResponse}"/> without touching this shared setup.
    /// </summary>
    private ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configureExtra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => CreateDbContext());
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CompleteBookingCommand).Assembly));

        configureExtra?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static BookingCompletionSweepService CreateSweepService(
        IServiceScopeFactory scopeFactory, TimeProvider timeProvider) =>
        new(
            scopeFactory,
            timeProvider,
            Options.Create(new BookingCompletionSweepOptions()),
            NullLogger<BookingCompletionSweepService>.Instance);

    private async Task<(Vehicle Vehicle, Customer Customer)> SeedVehicleAndCustomerAsync(
        AppDbContext dbContext, string emailLocalPart)
    {
        var vehicle = Vehicle.Create(
            $"CA{Guid.NewGuid():N}"[..8].ToUpperInvariant(), "Toyota", "Corolla", 2023, 350m);
        var customer = Customer.Create("Jane", "Doe", $"{emailLocalPart}@example.com", "0821234567");
        dbContext.Vehicles.Add(vehicle);
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();
        return (vehicle, customer);
    }

    [Fact]
    public async Task RunSweepAsync_ActiveBookingWithPastEndDate_CompletesIt()
    {
        await using var seedContext = CreateDbContext();
        var (vehicle, customer) = await SeedVehicleAndCustomerAsync(seedContext, "past-enddate");
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 1000m);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        await using var provider = BuildServiceProvider();
        var sweepService = CreateSweepService(provider.GetRequiredService<IServiceScopeFactory>(), TodayAfterBookings);

        await sweepService.RunSweepAsync(CancellationToken.None);

        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        reloaded.Status.Should().Be(BookingStatus.Completed);
    }

    [Fact]
    public async Task RunSweepAsync_ActiveBookingWithEndDateExactlyToday_CompletesIt()
    {
        await using var seedContext = CreateDbContext();
        var (vehicle, customer) = await SeedVehicleAndCustomerAsync(seedContext, "enddate-today");
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 10), 1000m);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        await using var provider = BuildServiceProvider();
        var sweepService = CreateSweepService(provider.GetRequiredService<IServiceScopeFactory>(), TodayAfterBookings);

        await sweepService.RunSweepAsync(CancellationToken.None);

        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        reloaded.Status.Should().Be(BookingStatus.Completed);
    }

    [Fact]
    public async Task RunSweepAsync_ActiveBookingWithFutureEndDate_LeavesItActive()
    {
        await using var seedContext = CreateDbContext();
        var (vehicle, customer) = await SeedVehicleAndCustomerAsync(seedContext, "future-enddate");
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1000m);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        await using var provider = BuildServiceProvider();
        var sweepService = CreateSweepService(provider.GetRequiredService<IServiceScopeFactory>(), TodayAfterBookings);

        await sweepService.RunSweepAsync(CancellationToken.None);

        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        reloaded.Status.Should().Be(BookingStatus.Active);
    }

    [Fact]
    public async Task RunSweepAsync_AlreadyCompletedBooking_NeverSelected_StaysCompleted()
    {
        await using var seedContext = CreateDbContext();
        var (vehicle, customer) = await SeedVehicleAndCustomerAsync(seedContext, "already-completed");
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 1000m);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();
        booking.Complete(TodayAfterBookings);
        await seedContext.SaveChangesAsync();

        await using var provider = BuildServiceProvider();
        var sweepService = CreateSweepService(provider.GetRequiredService<IServiceScopeFactory>(), TodayAfterBookings);

        await sweepService.RunSweepAsync(CancellationToken.None);

        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        reloaded.Status.Should().Be(BookingStatus.Completed);
    }

    [Fact]
    public async Task RunSweepAsync_CancelledBookingWithPastEndDate_NeverSelected_StaysCancelled()
    {
        await using var seedContext = CreateDbContext();
        var (vehicle, customer) = await SeedVehicleAndCustomerAsync(seedContext, "cancelled-past");
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 1000m);
        seedContext.Bookings.Add(booking);
        await seedContext.SaveChangesAsync();

        // Cancel() itself refuses a past-EndDate booking, so flip Status directly via raw SQL to
        // reach "Cancelled with a past EndDate" -- mirrors BookingMigrationTests' own technique for
        // simulating a state Cancel()'s own guard would never let a caller reach normally.
        await seedContext.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Bookings" SET "Status" = 'Cancelled' WHERE "Id" = {booking.Id}""");

        await using var provider = BuildServiceProvider();
        var sweepService = CreateSweepService(provider.GetRequiredService<IServiceScopeFactory>(), TodayAfterBookings);

        await sweepService.RunSweepAsync(CancellationToken.None);

        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        reloaded.Status.Should().Be(BookingStatus.Cancelled);
    }

    /// <summary>
    /// Proves spec-4-4's I/O matrix row "One booking's completion throws mid-sweep": with a
    /// test-only MediatR pipeline behavior injected that throws for exactly one of two eligible
    /// bookings, the other still completes and <see cref="BookingCompletionSweepService.RunSweepAsync"/>
    /// itself never throws. The real <see cref="IBookingRepository"/>/<see cref="ISender"/>/
    /// <see cref="CompleteBookingCommandHandler"/> chain runs throughout -- only the injected behavior
    /// simulates the failure, standing in for whatever a raced Cancel or a transient DB error would
    /// otherwise cause.
    /// </summary>
    [Fact]
    public async Task RunSweepAsync_OneBookingFailsMidSweep_StillCompletesTheOthers_AndNeverThrows()
    {
        await using var seedContext = CreateDbContext();
        var (vehicle1, customer1) = await SeedVehicleAndCustomerAsync(seedContext, "mid-sweep-failing");
        var (vehicle2, customer2) = await SeedVehicleAndCustomerAsync(seedContext, "mid-sweep-succeeding");

        var failingBooking = Booking.Create(
            vehicle1.Id, customer1.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 1000m);
        var succeedingBooking = Booking.Create(
            vehicle2.Id, customer2.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 1000m);
        seedContext.Bookings.AddRange(failingBooking, succeedingBooking);
        await seedContext.SaveChangesAsync();

        await using var provider = BuildServiceProvider(services =>
            services.AddScoped<IPipelineBehavior<CompleteBookingCommand, Unit>>(
                _ => new ThrowForBookingBehavior(failingBooking.Id)));
        var sweepService = CreateSweepService(provider.GetRequiredService<IServiceScopeFactory>(), TodayAfterBookings);

        var act = async () => await sweepService.RunSweepAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("one booking's failure must never propagate out of the sweep pass");

        await using var verifyContext = CreateDbContext();
        var reloadedFailing = await verifyContext.Bookings.SingleAsync(b => b.Id == failingBooking.Id);
        var reloadedSucceeding = await verifyContext.Bookings.SingleAsync(b => b.Id == succeedingBooking.Id);

        reloadedFailing.Status.Should().Be(
            BookingStatus.Active, "the failing booking's own completion must never have been committed");
        reloadedSucceeding.Status.Should().Be(
            BookingStatus.Completed, "the other eligible booking must still complete despite the first one's failure");
    }

    /// <summary>Test-only pipeline behavior that simulates one booking's completion throwing mid-sweep.</summary>
    private sealed class ThrowForBookingBehavior(Guid failingBookingId) : IPipelineBehavior<CompleteBookingCommand, Unit>
    {
        public Task<Unit> Handle(
            CompleteBookingCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken)
        {
            if (request.BookingId == failingBookingId)
            {
                throw new InvalidOperationException("Simulated failure for RunSweepAsync's isolation test.");
            }

            return next(cancellationToken);
        }
    }

    /// <summary>Minimal fixed-clock test double -- mirrors <c>BookingTests</c>/<c>CancelBookingCommandHandlerTests</c>'s own.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
