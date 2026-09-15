using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.CustomerSummaries.Dtos;

/// <summary>
/// The wire-shape projection backing the read-only Customer Summary page (spec-5-1): identity +
/// contact fields plus the customer's full, unpaginated booking history. Deliberately carries no
/// <c>IsDeleted</c> field (Scope decision 5 -- this is a customer-facing reference document, not an
/// admin view). Carries <see cref="IsAnonymized"/> (not a pre-rendered placeholder string) so the
/// page can render "Customer (anonymized)" itself, mirroring <c>BookingDto</c>'s exact
/// <c>CustomerIsAnonymized</c> convention. Mapping from the underlying domain entities happens only
/// via <see cref="FromDomain"/>, called from <c>GetCustomerSummaryQueryHandler</c> -- never in a
/// repository (AD-1/AD-5).
/// </summary>
public record CustomerSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    bool IsAnonymized,
    IReadOnlyList<CustomerSummaryBookingDto> Bookings)
{
    /// <summary>
    /// The one place a <see cref="Customer"/> (plus its full set of Booking+Vehicle rows) is
    /// projected to its wire shape (DRY). <paramref name="bookings"/> may be empty -- a customer
    /// with zero bookings still produces a valid summary, per spec-5-1's I/O matrix.
    /// </summary>
    public static CustomerSummaryDto FromDomain(
        Customer customer, IReadOnlyList<(Booking Booking, Vehicle Vehicle)> bookings) => new(
        customer.Id,
        customer.FirstName,
        customer.LastName,
        customer.Email,
        customer.PhoneNumber,
        customer.IsAnonymized,
        bookings.Select(row => CustomerSummaryBookingDto.FromDomain(row.Booking, row.Vehicle)).ToList());
}

/// <summary>
/// One row of the Customer Summary page's booking-history list -- denormalized with the Vehicle
/// display fields the read-only table needs, mirroring <c>BookingDto</c>'s exact denormalization
/// reasoning (AD-1: Booking itself carries no navigation properties). Deliberately carries no
/// Customer fields -- this record only ever appears nested inside a single <see cref="CustomerSummaryDto"/>
/// that already carries them once (DRY, no duplication across every row).
/// </summary>
public record CustomerSummaryBookingDto(
    Guid Id,
    string VehicleMake,
    string VehicleModel,
    string VehicleRegistrationNumber,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    BookingStatus Status)
{
    /// <summary>
    /// The one place a <see cref="Booking"/> (plus its referenced <see cref="Vehicle"/>) is
    /// projected to its wire shape for this page (DRY).
    /// </summary>
    public static CustomerSummaryBookingDto FromDomain(Booking booking, Vehicle vehicle) => new(
        booking.Id,
        vehicle.Make,
        vehicle.Model,
        vehicle.RegistrationNumber,
        booking.StartDate,
        booking.EndDate,
        booking.TotalPrice,
        booking.Status);
}
