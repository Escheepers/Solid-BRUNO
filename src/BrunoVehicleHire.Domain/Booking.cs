using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Domain;

/// <summary>
/// The Booking entity's lifecycle state (domain-model.md). <see cref="Booking.Create"/> always
/// assigns <see cref="Active"/> -- the Completed/Cancelled transitions belong to Epic 4's actual
/// Booking feature set (this story only needs a Booking that can exist, per spec-3-3's Scope
/// decision 1).
/// </summary>
public enum BookingStatus
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>
/// A genuinely minimal Booking entity (spec-3-3), grown by later Epic 4 stories: <see cref="Create"/>
/// (spec-3-3, the one context-free invariant a Booking can honestly validate at construction time),
/// <see cref="Cancel"/> (spec-4-3, the Active -> Cancelled transition), and <see cref="Complete"/>
/// (spec-4-4, the Active -> Completed transition, sweep-driven only) are the only ways to mutate a
/// valid instance; EF Core materializes existing rows via the private parameterless constructor,
/// bypassing no invariant since the row was valid when it was written. No property has a public
/// setter -- mirrors <see cref="Vehicle"/>/<see cref="Customer"/>'s exact pattern.
/// </summary>
public class Booking
{
    /// <summary>Longest rental allowed: a booking may span at most this many days.</summary>
    public const int MaxDurationDays = 365;

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public decimal TotalPrice { get; private set; }

    public BookingStatus Status { get; private set; }

    public DateTime CreatedDate { get; private set; }

    /// <summary>Reserved for EF Core materialization -- never call directly.</summary>
    private Booking()
    {
    }

    private Booking(
        Guid id,
        Guid vehicleId,
        Guid customerId,
        DateOnly startDate,
        DateOnly endDate,
        decimal totalPrice,
        DateTime createdDate)
    {
        Id = id;
        VehicleId = vehicleId;
        CustomerId = customerId;
        StartDate = startDate;
        EndDate = endDate;
        TotalPrice = totalPrice;
        Status = BookingStatus.Active;
        CreatedDate = createdDate;
    }

    /// <summary>
    /// Constructs a new, valid <see cref="Booking"/>, throwing <see cref="DomainRuleViolationException"/>
    /// if <paramref name="endDate"/> is not strictly after <paramref name="startDate"/> -- the only
    /// invariant this story can honestly validate (overlap prevention and the past-booking guard both
    /// need cross-row/Epic 4 logic this story doesn't build -- see spec-3-3's Design Notes).
    /// <see cref="Status"/> always starts at <see cref="BookingStatus.Active"/>, never a
    /// caller-supplied parameter. <paramref name="timeProvider"/> defaults to
    /// <see cref="TimeProvider.System"/> so tests can inject a fixed clock and assert an exact
    /// <see cref="CreatedDate"/>, mirroring <c>Vehicle.Create()</c>/<c>Customer.Create()</c>.
    /// </summary>
    public static Booking Create(
        Guid vehicleId,
        Guid customerId,
        DateOnly startDate,
        DateOnly endDate,
        decimal totalPrice,
        TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        ValidateInvariants(startDate, endDate);

        return new Booking(
            Guid.CreateVersion7(),
            vehicleId,
            customerId,
            startDate,
            endDate,
            totalPrice,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Moves an upcoming booking to new dates. Only an <see cref="BookingStatus.Active"/> booking whose
    /// <see cref="StartDate"/> is still after today (per <paramref name="timeProvider"/>) can be
    /// rescheduled -- exactly the window in which <see cref="Cancel"/> still works -- so a cancelled
    /// booking throws "Cannot edit — booking is cancelled." and a completed or already-started one
    /// throws "Cannot edit — booking has already started.". The new range must satisfy the same
    /// invariants as <see cref="Create"/> (end after start, within <see cref="MaxDurationDays"/>) and
    /// may not start in the past. <see cref="TotalPrice"/> is rescaled at the booking's ORIGINAL
    /// per-day rate (price / old days -- exact, since a price is always whole-cent rate x whole days),
    /// so a later change to the vehicle's rate never reprices an existing booking. Whether the new
    /// range clashes with ANOTHER booking on the vehicle is a cross-row rule, checked by the handler.
    /// On a thrown exception the booking is left unchanged.
    /// </summary>
    public void Reschedule(DateOnly newStartDate, DateOnly newEndDate, TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        if (Status == BookingStatus.Cancelled)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(Status), "Cannot edit — booking is cancelled.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (Status == BookingStatus.Completed || StartDate <= today)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(Status), "Cannot edit — booking has already started.");
        }

        if (newStartDate < today)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(StartDate), "StartDate cannot be in the past.");
        }

        ValidateInvariants(newStartDate, newEndDate);

        var dailyRate = TotalPrice / (EndDate.DayNumber - StartDate.DayNumber);

        StartDate = newStartDate;
        EndDate = newEndDate;
        TotalPrice = dailyRate * (newEndDate.DayNumber - newStartDate.DayNumber);
    }

    /// <summary>
    /// Transitions <see cref="Status"/> from <see cref="BookingStatus.Active"/> to
    /// <see cref="BookingStatus.Cancelled"/> (spec-4-3), never a physical delete (AD-16). Throws
    /// <see cref="DomainRuleViolationException"/> if the booking is already ineligible: an
    /// already-<see cref="BookingStatus.Cancelled"/> booking throws with "Cannot cancel — booking
    /// already cancelled."; an already-<see cref="BookingStatus.Completed"/> booking, or a
    /// still-<see cref="BookingStatus.Active"/> booking whose <see cref="StartDate"/> is on or before
    /// today (per <paramref name="timeProvider"/>), throws the identical "Cannot cancel — booking has
    /// already started." message -- a booking is only cancellable while it is genuinely still in the
    /// future (bugfix: spec-booking-form-error-handling-fixes.md). This covers a mid-rental booking
    /// (car already picked up), a past-EndDate-but-not-yet-swept booking, and a genuinely
    /// <see cref="BookingStatus.Completed"/> booking identically -- all three have necessarily already
    /// started. Since <see cref="EndDate"/> is always strictly after <see cref="StartDate"/> (enforced
    /// by <see cref="ValidateInvariants"/> at construction), <c>StartDate &lt;= today</c> is a strictly
    /// stronger check than the old <c>EndDate &lt;= today</c> one -- no previously-ineligible booking
    /// becomes eligible. No other field changes. <paramref name="timeProvider"/> defaults to
    /// <see cref="TimeProvider.System"/> so tests can inject a fixed clock, mirroring every other
    /// domain method's own convention.
    /// </summary>
    public void Cancel(TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        if (Status == BookingStatus.Cancelled)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(Status), "Cannot cancel — booking already cancelled.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (Status == BookingStatus.Completed || StartDate <= today)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(Status), "Cannot cancel — booking has already started.");
        }

        Status = BookingStatus.Cancelled;
    }

    /// <summary>
    /// Transitions <see cref="Status"/> from <see cref="BookingStatus.Active"/> to
    /// <see cref="BookingStatus.Completed"/> (spec-4-4), the sole path by which a booking is ever
    /// marked Completed -- always dispatched via <c>CompleteBookingCommand</c> from
    /// <c>BookingCompletionSweepService</c>, never directly reachable over HTTP (AD-17). Idempotent
    /// if already <see cref="BookingStatus.Completed"/> (a no-op, mirroring <c>Customer.SoftDelete</c>'s
    /// own precedent for a repeat call), but throws <see cref="DomainRuleViolationException"/> if
    /// <see cref="BookingStatus.Cancelled"/> -- completing a cancelled booking would misrepresent it.
    /// The eligibility boundary is the exact complement of <see cref="Cancel"/>'s own: <c>Cancel</c>
    /// already treats <see cref="EndDate"/> on or before today as "this booking is over" and refuses
    /// to cancel it; <see cref="Complete"/> requires that same condition to actually transition,
    /// throwing (defense-in-depth) if <see cref="EndDate"/> is still in the future -- guarding against
    /// the sweep (or any other caller) completing a booking prematurely. No other field changes.
    /// <paramref name="timeProvider"/> defaults to <see cref="TimeProvider.System"/> so tests can
    /// inject a fixed clock, mirroring <see cref="Cancel"/>'s own convention.
    /// </summary>
    public void Complete(TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        if (Status == BookingStatus.Completed)
        {
            return;
        }

        if (Status == BookingStatus.Cancelled)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(Status), "Cannot complete — booking already cancelled.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (EndDate > today)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(Status), "Cannot complete — booking has not ended yet.");
        }

        Status = BookingStatus.Completed;
    }

    /// <summary>
    /// The single shared source of every Booking invariant this story validates, called by
    /// <see cref="Create"/>. Throws <see cref="DomainRuleViolationException"/> on the first violated
    /// invariant; never mutates any Booking instance itself.
    /// </summary>
    private static void ValidateInvariants(DateOnly startDate, DateOnly endDate)
    {
        if (endDate <= startDate)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(EndDate), "EndDate must be after StartDate.");
        }

        if (endDate.DayNumber - startDate.DayNumber > MaxDurationDays)
        {
            throw new DomainRuleViolationException(
                nameof(Booking), nameof(EndDate), $"A booking cannot be longer than {MaxDurationDays} days.");
        }
    }
    
}
