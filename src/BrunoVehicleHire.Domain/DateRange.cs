namespace BrunoVehicleHire.Domain;

/// <summary>
/// A pure, infrastructure-free value object representing a half-open date interval
/// <c>[Start, End)</c> -- domain-model.md's own suggested DDD exercise (spec-4-2). Deliberately NOT
/// woven into <see cref="Booking"/>'s own <c>StartDate</c>/<c>EndDate</c> properties (spec-4-2's Scope
/// decision 1): it is constructed ad hoc from a booking's own dates wherever an overlap check runs,
/// leaving <c>Booking.Create</c>'s signature (Story 3.3) completely unchanged.
/// </summary>
/// <param name="Start">The range's inclusive start date.</param>
/// <param name="End">The range's exclusive end date.</param>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    /// <summary>
    /// True if this range and <paramref name="other"/> share at least one day, using half-open
    /// interval semantics (AD-7, same-day turnover): <c>Start &lt; other.End &amp;&amp; other.Start
    /// &lt; End</c>. Touching endpoints -- one range's <c>End</c> equal to the other's <c>Start</c> --
    /// are NOT an overlap, so a same-day turnover (one booking ending the day another begins) is
    /// always allowed.
    /// </summary>
    public bool Overlaps(DateRange other) => Start < other.End && other.Start < End;
}
