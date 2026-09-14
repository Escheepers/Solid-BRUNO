using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BrunoVehicleHire.Infrastructure.BackgroundServices;

/// <summary>
/// Realizes AD-17: on a configurable timer (<see cref="BookingCompletionSweepOptions.Interval"/>),
/// finds every <c>Active</c> booking whose <c>EndDate</c> has passed and dispatches a
/// <see cref="CompleteBookingCommand"/> for each -- a normal Command through the normal MediatR
/// pipeline, mutating via <c>Booking.Complete()</c>. This is the only path that ever sets
/// <c>Status: Completed</c>; no Query handler ever writes (spec-4-4's Boundaries).
///
/// <para>
/// This service itself is registered as a singleton (the default for <c>AddHostedService</c>), but
/// <c>IBookingRepository</c>/<c>ISender</c> are scoped -- so <see cref="RunSweepAsync"/> resolves them
/// from a fresh <see cref="IServiceScopeFactory"/>-created scope per sweep pass, exactly like a normal
/// HTTP request would, rather than holding a scoped dependency directly (which a singleton must never
/// do).
/// </para>
///
/// <para>
/// <see cref="ExecuteAsync"/> is a thin loop -- call <see cref="RunSweepAsync"/>, then wait one
/// interval, repeat -- deliberately left untested directly (see spec-4-4's Design Notes): proving the
/// timer mechanics themselves work would require either waiting on a real interval or mocking
/// <c>Task.Delay</c>, both of which the AC steers away from. All the logic actually worth testing
/// (which bookings get completed, and that one failure never blocks the rest of the pass) lives in
/// <see cref="RunSweepAsync"/>, directly callable by tests with a fixed <see cref="TimeProvider"/> and
/// no timer involved at all.
/// </para>
/// </summary>
public class BookingCompletionSweepService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<BookingCompletionSweepOptions> options,
    ILogger<BookingCompletionSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSweepAsync(stoppingToken);

            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    /// <summary>
    /// The sweep's actual logic (spec-4-4's Scope decision 4), directly callable by tests with no
    /// timer involved. Resolves a scope, reads every sweep-eligible booking as of "today" (per
    /// <paramref name="cancellationToken"/> and this instance's injected <see cref="TimeProvider"/>),
    /// and dispatches a <see cref="CompleteBookingCommand"/> per booking. A failure completing one
    /// booking is caught and logged here, not allowed to propagate (Scope decision 5) -- an unhandled
    /// exception in <see cref="ExecuteAsync"/> would crash the whole host, so one raced/failed booking
    /// must never block the rest of that sweep pass.
    /// </summary>
    public async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var eligibleBookings = await repository.GetActivePastEndDateAsync(today, cancellationToken);

        foreach (var booking in eligibleBookings)
        {
            try
            {
                await sender.Send(new CompleteBookingCommand(booking.Id), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "Booking completion sweep failed to complete booking {BookingId}.", booking.Id);
            }
        }
    }
}
