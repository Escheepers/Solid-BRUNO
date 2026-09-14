using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Bookings.Dtos;

/// <summary>
/// The wire-shape projection of a <see cref="Booking"/> aggregate, denormalized with the
/// Vehicle/Customer display fields the Bookings list needs (spec-4-1's Boundaries: "no navigation
/// properties on the Booking domain entity itself" -- AD-1, aggregates reference each other only by
/// Id). Mapping from the three domain entities happens only via <see cref="FromDomain"/>, called
/// from handlers -- never in the repository (AD-1/AD-5), mirroring <c>VehicleDto</c>/<c>CustomerDto</c>'s
/// exact pattern. Carries the Customer's <see cref="Customer.IsAnonymized"/> flag (not FirstName/
/// LastName placeholders) so the list can render "Customer (anonymized)" itself -- see spec-4-1's
/// I/O matrix row for a booking whose customer is now inactive/anonymized.
/// </summary>
public record BookingDto(
    Guid Id,
    Guid VehicleId,
    string VehicleMake,
    string VehicleModel,
    string VehicleRegistrationNumber,
    Guid CustomerId,
    string CustomerFirstName,
    string CustomerLastName,
    bool CustomerIsAnonymized,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    BookingStatus Status,
    DateTime CreatedDate)
{
    /// <summary>
    /// The one place a <see cref="Booking"/> (plus its referenced <see cref="Vehicle"/>/
    /// <see cref="Customer"/>) is projected to its wire shape, shared by every query/command handler
    /// that returns a <see cref="BookingDto"/> (DRY).
    /// </summary>
    public static BookingDto FromDomain(Booking booking, Vehicle vehicle, Customer customer) => new(
        booking.Id,
        booking.VehicleId,
        vehicle.Make,
        vehicle.Model,
        vehicle.RegistrationNumber,
        booking.CustomerId,
        customer.FirstName,
        customer.LastName,
        customer.IsAnonymized,
        booking.StartDate,
        booking.EndDate,
        booking.TotalPrice,
        booking.Status,
        booking.CreatedDate);
}
