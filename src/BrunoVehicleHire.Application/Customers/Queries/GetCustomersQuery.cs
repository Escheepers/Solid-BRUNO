using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Queries;

/// <summary>
/// Requests one page of customers, optionally filtered by a free-text search across
/// FirstName/LastName (never Email/PhoneNumber -- those are encrypted; see spec-3-1's Scope
/// decision 2). Validated by <see cref="GetCustomersQueryValidator"/> through the shared
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline -- never validated manually
/// in the handler. Mirrors <c>GetVehiclesQuery</c>'s shape, minus <c>IncludeInactive</c> -- no
/// Restore exists yet for Customer (YAGNI; Story 3.2+).
/// </summary>
public record GetCustomersQuery(int Page, int PageSize, string? Search) : IRequest<PagedResult<CustomerDto>>;
