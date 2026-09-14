using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Application.Customers.Dtos;

/// <summary>
/// The wire-shape projection of a <see cref="Customer"/> aggregate returned by every Customer query
/// and mutation. Mapping from the domain entity happens only via <see cref="FromDomain"/>, called
/// from handlers -- never in the repository (AD-1/AD-5), which stays ignorant of Application-layer
/// DTO shapes. Mirrors <c>VehicleDto</c>'s exact shape.
/// </summary>
public record CustomerDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    DateTime CreatedDate,
    bool IsDeleted,
    bool IsAnonymized)
{
    /// <summary>
    /// The one place a <see cref="Customer"/> is projected to its wire shape, shared by every
    /// query/command handler that returns a <see cref="CustomerDto"/> (DRY).
    /// </summary>
    public static CustomerDto FromDomain(Customer customer) => new(
        customer.Id,
        customer.FirstName,
        customer.LastName,
        customer.Email,
        customer.PhoneNumber,
        customer.CreatedDate,
        customer.IsDeleted,
        customer.IsAnonymized);
}
