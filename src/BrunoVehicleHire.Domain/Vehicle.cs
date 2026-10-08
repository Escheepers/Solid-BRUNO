using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Domain;

/// <summary>
/// The Vehicle aggregate root. The only way to construct a valid instance is
/// <see cref="Create"/>, which enforces every invariant below; EF Core materializes
/// existing rows via the private parameterless constructor, bypassing no invariant
/// since the row was valid when it was written. No property has a public setter --
/// state changes only through aggregate methods (e.g. <see cref="SoftDelete"/>).
/// </summary>
public class Vehicle
{
    private const int EarliestPlausibleYear = 1900;

    /// <summary>
    /// Upper bound on <see cref="DailyRate"/>: far above any real rental rate, and low enough that
    /// <c>DailyRate x days</c> always fits the <c>numeric(18,2)</c> price columns.
    /// </summary>
    public const decimal MaxDailyRate = 1_000_000m;

    /// <summary>
    /// The one canonical form of a registration number -- trimmed and upper-cased -- so "ca 123 456",
    /// "CA 123 456" and " CA 123 456 " are the same vehicle for the unique-registration rule. Used by
    /// <see cref="Create"/>/<see cref="Update"/> when storing and by the create/update handlers for the
    /// duplicate pre-check, so both always compare the same form.
    /// </summary>
    public static string NormalizeRegistrationNumber(string registrationNumber)
    {
        return registrationNumber.Trim().ToUpperInvariant();
    }

    /// <summary>Whether <paramref name="dailyRate"/> is a whole number of cents (at most 2 decimals).</summary>
    public static bool IsWholeCents(decimal dailyRate)
    {
        return decimal.Round(dailyRate, 2) == dailyRate;
    }

    public Guid Id { get; private set; }

    public string RegistrationNumber { get; private set; } = string.Empty;

    public string Make { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public decimal DailyRate { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedDate { get; private set; }

    /// <summary>Reserved for EF Core materialization -- never call directly.</summary>
    private Vehicle()
    {
    }

    private Vehicle(
        Guid id,
        string registrationNumber,
        string make,
        string model,
        int year,
        decimal dailyRate,
        DateTime createdDate)
    {
        Id = id;
        RegistrationNumber = registrationNumber;
        Make = make;
        Model = model;
        Year = year;
        DailyRate = dailyRate;
        IsDeleted = false;
        CreatedDate = createdDate;
    }

    /// <summary>
    /// Constructs a new, valid <see cref="Vehicle"/>, throwing <see cref="DomainRuleViolationException"/>
    /// if any invariant is violated. <paramref name="timeProvider"/> defaults to <see cref="TimeProvider.System"/>
    /// so tests can inject a fixed clock and assert an exact <see cref="CreatedDate"/>.
    /// </summary>
    public static Vehicle Create(
        string registrationNumber,
        string make,
        string model,
        int year,
        decimal dailyRate,
        TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        ValidateInvariants(registrationNumber, make, model, year, dailyRate, timeProvider);

        return new Vehicle(
            Guid.CreateVersion7(),
            NormalizeRegistrationNumber(registrationNumber),
            make,
            model,
            year,
            dailyRate,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Reassigns every mutable field, throwing <see cref="DomainRuleViolationException"/> if any
    /// invariant is violated -- the exact same invariants <see cref="Create"/> enforces, via the
    /// shared <see cref="ValidateInvariants"/> validator (DRY). On a thrown exception the vehicle is
    /// left entirely unchanged (validation runs before any property is reassigned).
    /// <paramref name="timeProvider"/> defaults to <see cref="TimeProvider.System"/> so tests can
    /// inject a fixed clock for the Year plausible-range check.
    /// </summary>
    public void Update(
        string registrationNumber,
        string make,
        string model,
        int year,
        decimal dailyRate,
        TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        ValidateInvariants(registrationNumber, make, model, year, dailyRate, timeProvider);

        RegistrationNumber = NormalizeRegistrationNumber(registrationNumber);
        Make = make;
        Model = model;
        Year = year;
        DailyRate = dailyRate;
    }

    /// <summary>Marks the vehicle deleted. Idempotent: calling it again is a no-op, not an error.</summary>
    public void SoftDelete()
    {
        IsDeleted = true;
    }

    /// <summary>
    /// Clears the soft-delete flag. Deliberately the OPPOSITE of <see cref="SoftDelete"/>'s
    /// idempotency: restoring a vehicle that is already active throws
    /// <see cref="DomainRuleViolationException"/> rather than silently succeeding, per the
    /// AC ("the action fails gracefully... rather than silently double-processing"). No other
    /// field changes.
    /// </summary>
    public void Restore()
    {
        if (!IsDeleted)
        {
            throw new DomainRuleViolationException(nameof(Vehicle), nameof(IsDeleted), "Already active.");
        }

        IsDeleted = false;
    }

    /// <summary>
    /// The single shared source of every Vehicle invariant, called by both <see cref="Create"/> and
    /// <see cref="Update"/> (DRY -- previously these four checks lived duplicated inline in each).
    /// Throws <see cref="DomainRuleViolationException"/> on the first violated invariant; never
    /// mutates any Vehicle instance itself.
    /// </summary>
    private static void ValidateInvariants(
        string registrationNumber,
        string make,
        string model,
        int year,
        decimal dailyRate,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber))
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(RegistrationNumber), "RegistrationNumber must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(make))
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(Make), "Make must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(Model), "Model must not be blank.");
        }

        if (dailyRate <= 0)
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(DailyRate), "DailyRate must be positive.");
        }

        if (dailyRate > MaxDailyRate)
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(DailyRate), $"DailyRate must not exceed {MaxDailyRate:N0}.");
        }

        if (!IsWholeCents(dailyRate))
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(DailyRate), "DailyRate must have at most 2 decimal places.");
        }

        var nextCalendarYear = timeProvider.GetUtcNow().Year + 1;
        if (year < EarliestPlausibleYear || year > nextCalendarYear)
        {
            throw new DomainRuleViolationException(
                nameof(Vehicle), nameof(Year), $"Year must be between {EarliestPlausibleYear} and {nextCalendarYear}.");
        }
    }
}
