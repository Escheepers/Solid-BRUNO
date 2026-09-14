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
/// A genuinely minimal Booking entity (spec-3-3) -- just enough to construct a valid row for
/// test-seeding and to query "does this customer have any bookings". The only way to construct a
/// valid instance is <see cref="Create"/>, which enforces the one context-free invariant this story
/// can honestly validate; EF Core materializes existing rows via the private parameterless
/// constructor, bypassing no invariant since the row was valid when it was written. No property has
/// a public setter -- mirrors <see cref="Vehicle"/>/<see cref="Customer"/>'s exact pattern.
/// Deliberately NOT built: overlap prevention (needs cross-row Vehicle-availability logic), the
/// past-booking delete guard, and any Cancel/Complete transition -- all belong to Epic 4's actual
/// Booking feature set, not this story.
/// </summary>
public class Booking
{
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
    }
}
