using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Application.Common;

/// <summary>
/// Turns a <c>DbUpdateConcurrencyException</c> (the xmin token rejecting a save because another request
/// changed the row first) into the right clean 4xx, instead of letting it escape as a 500. After a
/// failed save the handler re-reads the row through its own repository: if it is no longer visible
/// (the other request deleted/deactivated it) the answer is the same 404 a sequential second request
/// would get; if it is still there, something else changed it, so the caller is told to reload and
/// retry (409). Deliberately does NOT re-apply the caller's change on top of the other one: a retry
/// would silently overwrite someone else's edit, and for Erase it could report success without the
/// personal data actually being scrubbed.
/// </summary>
public static class ConcurrencyConflict
{
    public const string Rule = "ConcurrencyConflict";

    public static DomainRuleViolationException For(string entity) =>
        new(entity, Rule, $"This {entity.ToLowerInvariant()} was changed by someone else just now. Reload and try again.");

    public static async Task<Exception> ResolveAsync<TEntity>(
        string entity, Guid id, Func<Task<TEntity?>> reRead)
        where TEntity : class =>
        await reRead() is null ? new NotFoundException(entity, id) : For(entity);
}
