using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Domain;

/// <summary>
/// The Customer aggregate root. The only way to construct a valid instance is
/// <see cref="Create"/>, which enforces every invariant below; EF Core materializes existing rows
/// via the private parameterless constructor, bypassing no invariant since the row was valid when
/// it was written. No property has a public setter -- state changes only through aggregate methods
/// (none exist yet; Edit/Deactivate/Restore/Anonymize arrive in Stories 3.2-3.5). Deliberately
/// framework-free (AD-1): this type knows nothing about encryption, hashing, or persistence --
/// those are entirely <c>AppDbContext</c>'s concern (Infrastructure).
/// </summary>
public class Customer
{
    public Guid Id { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PhoneNumber { get; private set; } = string.Empty;

    public bool IsDeleted { get; private set; }

    public bool IsAnonymized { get; private set; }

    public DateTime CreatedDate { get; private set; }

    /// <summary>Reserved for EF Core materialization -- never call directly.</summary>
    private Customer()
    {
    }

    private Customer(
        Guid id,
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        DateTime createdDate)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
        IsDeleted = false;
        IsAnonymized = false;
        CreatedDate = createdDate;
    }

    /// <summary>
    /// Constructs a new, valid <see cref="Customer"/>, throwing <see cref="DomainRuleViolationException"/>
    /// if any invariant is violated. <paramref name="timeProvider"/> defaults to
    /// <see cref="TimeProvider.System"/> so tests can inject a fixed clock and assert an exact
    /// <see cref="CreatedDate"/>. Deliberately does NOT validate <paramref name="email"/>'s FORMAT --
    /// that is <c>CreateCustomerCommandValidator</c>'s job (a 400-level shape check), not a
    /// domain invariant (409-level) -- mirrors <c>Vehicle.Create()</c>'s Year split.
    /// </summary>
    public static Customer Create(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        ValidateInvariants(firstName, lastName, email, phoneNumber);

        return new Customer(
            Guid.CreateVersion7(),
            firstName,
            lastName,
            email,
            phoneNumber,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Reassigns every mutable field, throwing <see cref="DomainRuleViolationException"/> if any
    /// invariant is violated -- the exact same invariants <see cref="Create"/> enforces, via the
    /// shared <see cref="ValidateInvariants"/> validator (DRY). On a thrown exception the customer is
    /// left entirely unchanged (validation runs before any property is reassigned). No
    /// <c>TimeProvider</c> parameter, unlike <c>Vehicle.Update()</c> -- Customer has no
    /// time-dependent invariant to check (unlike Vehicle's Year), so accepting one here would be
    /// an unused parameter kept only for superficial signature symmetry (YAGNI).
    /// </summary>
    public void Update(
        string firstName,
        string lastName,
        string email,
        string phoneNumber)
    {
        ValidateInvariants(firstName, lastName, email, phoneNumber);

        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Marks the customer deleted. Idempotent: calling it again is a no-op, not an error.</summary>
    public void SoftDelete()
    {
        IsDeleted = true;
    }

    /// <summary>
    /// Clears the soft-delete flag. Deliberately the OPPOSITE of <see cref="SoftDelete"/>'s
    /// idempotency: restoring a customer that is already active throws
    /// <see cref="DomainRuleViolationException"/> rather than silently succeeding -- mirrors
    /// <see cref="Vehicle.Restore"/>'s exact message/shape. No other field changes.
    /// </summary>
    public void Restore()
    {
        if (!IsDeleted)
        {
            throw new DomainRuleViolationException(nameof(Customer), nameof(IsDeleted), "Already active.");
        }

        IsDeleted = false;
    }

    /// <summary>
    /// The single shared source of every Customer invariant, called by <see cref="Create"/>. Throws
    /// <see cref="DomainRuleViolationException"/> on the first violated invariant; never mutates any
    /// Customer instance itself.
    /// </summary>
    private static void ValidateInvariants(string firstName, string lastName, string email, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new DomainRuleViolationException(
                nameof(Customer), nameof(FirstName), "FirstName must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainRuleViolationException(
                nameof(Customer), nameof(LastName), "LastName must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainRuleViolationException(
                nameof(Customer), nameof(Email), "Email must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new DomainRuleViolationException(
                nameof(Customer), nameof(PhoneNumber), "PhoneNumber must not be blank.");
        }
    }
}
