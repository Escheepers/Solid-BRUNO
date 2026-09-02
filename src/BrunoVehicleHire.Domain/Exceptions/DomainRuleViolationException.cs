namespace BrunoVehicleHire.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant is violated (e.g. constructing a <see cref="Vehicle"/>
/// with a blank RegistrationNumber). Shared across every aggregate's business rules
/// (Vehicle now; Customer, Booking later) so callers can catch/map one exception type.
/// Story 1.5's global exception handler maps this generically to an HTTP 409 -- this
/// type itself must stay framework-free since it lives in Domain.
/// </summary>
public class DomainRuleViolationException : Exception
{
    public string Entity { get; }

    public string Rule { get; }

    public DomainRuleViolationException(string entity, string rule, string message)
        : base(message)
    {
        Entity = entity;
        Rule = rule;
    }
}
