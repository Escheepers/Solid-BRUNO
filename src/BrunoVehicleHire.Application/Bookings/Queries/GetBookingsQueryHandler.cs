using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Queries;

/// <summary>
/// Calls <see cref="IBookingRepository.GetPagedAsync"/>, maps each <c>(Booking, Vehicle, Customer)</c>
/// tuple to a <see cref="BookingDto"/>, and wraps the page in the shared <see cref="PagedResult{T}"/>
/// shape. Never validates <see cref="GetBookingsQuery"/> itself -- that already happened in the
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> pipeline stage before this handler runs.
/// </summary>
public class GetBookingsQueryHandler(IBookingRepository repository)
    : IRequestHandler<GetBookingsQuery, PagedResult<BookingDto>>
{
    public async Task<PagedResult<BookingDto>> Handle(GetBookingsQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await repository.GetPagedAsync(request.Page, request.PageSize, cancellationToken);

        var dtos = items
            .Select(item => BookingDto.FromDomain(item.Booking, item.Vehicle, item.Customer))
            .ToList();

        return new PagedResult<BookingDto>(dtos, totalCount, request.Page, request.PageSize);
    }
}
