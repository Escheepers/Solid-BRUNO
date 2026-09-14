using BrunoVehicleHire.Application.Bookings.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Queries;

/// <summary>
/// Requests a single Booking's full record by id, for the detail view (spec-4-5), mirroring
/// <c>GetVehicleByIdQuery</c>'s exact shape. Reuses the existing <see cref="BookingDto"/> shape
/// (spec-4-5's Scope decision 2) -- no new DTO for the detail view.
/// </summary>
public record GetBookingByIdQuery(Guid BookingId) : IRequest<BookingDto>;
