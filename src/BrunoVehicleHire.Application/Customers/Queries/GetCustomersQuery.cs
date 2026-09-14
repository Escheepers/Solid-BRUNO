using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Queries;

/// <summary>
/// Requests one page of customers, optionally filtered by a free-text search across
/// FirstName/LastName (never Email/PhoneNumber -- those are encrypted; see spec-3-1's Scope
/// decision 2). Validated by <see cref="GetCustomersQueryValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler. Mirrors <c>GetVehiclesQuery</c>'s exact shape, including <c>IncludeInactive</c>
/// (Story 3.4, mirroring Vehicle's exact Story-2.4 addition) -- defaults to <c>false</c> so every
/// pre-existing call site's behavior is unchanged.
/// </summary>
public record GetCustomersQuery(int Page, int PageSize, string? Search, bool IncludeInactive = false)
    : IRequest<PagedResult<CustomerDto>>;
