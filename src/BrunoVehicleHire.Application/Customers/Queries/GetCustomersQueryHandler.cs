using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.Customers.Queries;

/// <summary>
/// Calls <see cref="ICustomerRepository.GetPagedAsync"/>, maps each domain <c>Customer</c> to a
/// <see cref="CustomerDto"/>, and wraps the page in the shared <see cref="PagedResult{T}"/> shape.
/// Never validates <see cref="GetCustomersQuery"/> itself -- that already happened in the
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> pipeline stage before this handler runs.
/// Mirrors <c>GetVehiclesQueryHandler</c>'s exact shape.
/// </summary>
public class GetCustomersQueryHandler(ICustomerRepository repository)
    : IRequestHandler<GetCustomersQuery, PagedResult<CustomerDto>>
{
    public async Task<PagedResult<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await repository.GetPagedAsync(
            request.Page, request.PageSize, request.Search, cancellationToken);

        var dtos = items
            .Select(CustomerDto.FromDomain)
            .ToList();

        return new PagedResult<CustomerDto>(dtos, totalCount, request.Page, request.PageSize);
    }
}
