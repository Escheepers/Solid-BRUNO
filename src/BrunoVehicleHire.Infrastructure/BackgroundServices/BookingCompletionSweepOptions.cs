namespace BrunoVehicleHire.Infrastructure.BackgroundServices;

/// <summary>
/// Options bound from the <c>"BookingCompletionSweep"</c> configuration section (spec-4-4's
/// Boundaries: the sweep interval is configurable via <c>IOptions&lt;BookingCompletionSweepOptions&gt;</c>,
/// never hardcoded). <see cref="Interval"/> defaults to 15 minutes if the section/key is absent,
/// mirroring <c>ApiKeyOptions</c>'s plain-options-class shape.
/// </summary>
public class BookingCompletionSweepOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
}
