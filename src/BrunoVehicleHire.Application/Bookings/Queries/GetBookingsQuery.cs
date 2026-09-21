using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using MediatR;

namespace BrunoVehicleHire.Application.Bookings.Queries;

/// <summary>
/// Requests one page of bookings. Deliberately carries no <c>IncludeInactive</c> parameter, unlike
/// <c>GetVehiclesQuery</c>/<c>GetCustomersQuery</c> -- spec-4-1's Scope decision 4: every booking is
/// always shown regardless of its referenced Vehicle/Customer's active state. Validated by
/// <see cref="GetBookingsQueryValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler. <c>VehicleId</c> is an additive, optional filter (spec-4-5's Scope decision 1),
/// mirroring the established optional-query-param pattern (<c>IncludeInactive</c> etc.): when
/// <c>null</c> (the default), every booking is returned exactly as before; when set, only bookings
/// referencing that vehicle are returned. Reused unfiltered by the Bookings list and filtered by
/// Vehicle Detail's booking-history section, rather than a second, near-duplicate query.
/// <c>Search</c> (spec-bookings-search) is a second, independent optional filter: when <c>null</c>
/// or absent (the default), behavior is completely unchanged; when set, only bookings whose Vehicle's
/// Make/Model/RegistrationNumber or Customer's FirstName/LastName match the term (case-insensitive
/// substring, OR'd across all five fields) are returned. Composable with <c>VehicleId</c> -- Vehicle
/// Detail's own call site continues passing only <c>VehicleId</c>, leaving <c>Search</c> <c>null</c>.
/// </summary>
public record GetBookingsQuery(int Page, int PageSize, Guid? VehicleId = null, string? Search = null)
    : IRequest<PagedResult<BookingDto>>;
