using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Queries;

/// <summary>
/// Requests one page of bookings. Deliberately carries no <c>Search</c>/<c>IncludeInactive</c>
/// parameter, unlike <c>GetVehiclesQuery</c>/<c>GetCustomersQuery</c> -- spec-4-1's Scope decision 3:
/// nothing in this story's AC names a field to search, and every booking is always shown regardless
/// of its referenced Vehicle/Customer's active state (Scope decision 4). Validated by
/// <see cref="GetBookingsQueryValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler.
/// </summary>
public record GetBookingsQuery(int Page, int PageSize) : IRequest<PagedResult<BookingDto>>;
