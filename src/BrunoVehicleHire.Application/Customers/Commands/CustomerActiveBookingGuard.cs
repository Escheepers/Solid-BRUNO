using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// The one definition of "a customer with an Active booking can be neither deactivated nor erased",
/// shared by <see cref="SoftDeleteCustomerCommandHandler"/> and <see cref="AnonymizeCustomerCommandHandler"/>
/// so the rule and its message can never drift apart. Mirrors the vehicle rule in
/// <c>SoftDeleteVehicleCommandHandler</c> (same Active-only definition, same message shape); the
/// customer Hard Delete rule stays stricter (ANY booking blocks it -- see
/// <c>HardDeleteCustomerCommandHandler</c>).
/// </summary>
internal static class CustomerActiveBookingGuard
{
    public static async Task EnsureNoActiveBookingAsync(
        IBookingRepository bookingRepository, Guid customerId, CancellationToken cancellationToken)
    {
        if (await bookingRepository.ExistsActiveForCustomerAsync(customerId, cancellationToken))
        {
            throw new DomainRuleViolationException(
                "Customer",
                "HasActiveBookings",
                "This customer has an active or upcoming booking — cancel it first, or wait for it to complete.");
        }
    }
}
