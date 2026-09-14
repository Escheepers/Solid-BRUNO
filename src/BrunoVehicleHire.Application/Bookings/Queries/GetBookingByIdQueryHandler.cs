using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Queries;

/// <summary>
/// Orchestrates <see cref="GetBookingByIdQuery"/>:
/// <see cref="IBookingRepository.GetByIdWithVehicleAndCustomerAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) -> <see cref="BookingDto.FromDomain"/>.
/// Contains no business logic of its own beyond that orchestration (SRP) -- mirrors
/// <c>GetVehicleByIdQueryHandler</c>'s exact shape. Reuses the exact dual-<c>IgnoreQueryFilters()</c>
/// join <c>GetBookingsQueryHandler</c> already relies on via
/// <see cref="IBookingRepository.GetByIdWithVehicleAndCustomerAsync"/> (spec-4-5's Scope decision 2)
/// so a booking whose Customer has since been anonymized (or whose Vehicle has since been
/// soft-deleted) still resolves correctly here too, exactly as it already does in the list.
/// </summary>
public class GetBookingByIdQueryHandler(IBookingRepository repository)
    : IRequestHandler<GetBookingByIdQuery, BookingDto>
{
    public async Task<BookingDto> Handle(GetBookingByIdQuery request, CancellationToken cancellationToken)
    {
        var (booking, vehicle, customer) = await repository.GetByIdWithVehicleAndCustomerAsync(
            request.BookingId, cancellationToken)
            ?? throw new NotFoundException(nameof(Booking), request.BookingId);

        return BookingDto.FromDomain(booking, vehicle, customer);
    }
}
