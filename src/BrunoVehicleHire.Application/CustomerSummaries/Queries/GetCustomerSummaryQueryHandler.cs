using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.CustomerSummaries.Dtos;
using BrunoVehicleHire.Domain;
using MediatR;

namespace BrunoVehicleHire.Application.CustomerSummaries.Queries;

/// <summary>
/// Orchestrates <see cref="GetCustomerSummaryQuery"/>:
/// <see cref="ICustomerRepository.GetByIdIncludingSoftDeletedAsync"/> (throwing
/// <see cref="NotFoundException"/> if no row exists at all for this id) ->
/// <see cref="IBookingRepository.GetForCustomerWithVehicleAsync"/> -> <see cref="CustomerSummaryDto.FromDomain"/>.
/// Contains no business logic of its own beyond that orchestration (SRP), mirroring
/// <c>GetBookingByIdQueryHandler</c>'s exact shape. Deliberately uses
/// <c>GetByIdIncludingSoftDeletedAsync</c> rather than the filtered <c>GetByIdAsync</c> (per
/// Boundaries): a deactivated (soft-deleted) customer's summary must still render, not 404 --
/// only a genuinely nonexistent id throws.
/// </summary>
public class GetCustomerSummaryQueryHandler(
    ICustomerRepository customerRepository, IBookingRepository bookingRepository)
    : IRequestHandler<GetCustomerSummaryQuery, CustomerSummaryDto>
{
    public async Task<CustomerSummaryDto> Handle(GetCustomerSummaryQuery request, CancellationToken cancellationToken)
    {
        var customer = await customerRepository.GetByIdIncludingSoftDeletedAsync(
            request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        var bookings = await bookingRepository.GetForCustomerWithVehicleAsync(
            request.CustomerId, cancellationToken);

        return CustomerSummaryDto.FromDomain(customer, bookings);
    }
}
