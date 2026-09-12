namespace BrunoVehicleHire.Application.Common;

/// <summary>
/// The one seam through which any command commits its changes (AD-5): repositories never call
/// <c>SaveChangesAsync</c> themselves, only the handler does, and only once per request, after
/// every repository call for that request has queued its change. Deliberately introduced only now
/// (Story 2.1) -- Story 1.7's Design Notes deferred it until a real write existed to justify it.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
