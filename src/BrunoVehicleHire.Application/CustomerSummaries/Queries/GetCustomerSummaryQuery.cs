using BrunoVehicleHire.Application.CustomerSummaries.Dtos;
using MediatR;

namespace BrunoVehicleHire.Application.CustomerSummaries.Queries;

/// <summary>
/// Requests a single Customer's read-only summary -- identity + contact + full booking history --
/// for the Customer Summary page (spec-5-1), mirroring <c>GetBookingByIdQuery</c>'s exact shape.
/// Deliberately its own <c>CustomerSummaries/Queries</c> slice rather than living alongside
/// <c>Customers.Queries.GetCustomersQuery</c> (Scope decision 3): this read model spans both the
/// Customer and Booking aggregates, and epics.md calls the summary out as "its own distinct
/// concern... not bundled into either the Customer or Booking epic's CRUD scope."
/// </summary>
public record GetCustomerSummaryQuery(Guid CustomerId) : IRequest<CustomerSummaryDto>;
