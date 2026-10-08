using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrunoVehicleHire.Application.Bookings.Commands;

/// <summary>
/// Orchestrates <see cref="UpdateBookingCommand"/>: <see cref="IBookingRepository.GetByIdWithVehicleAndCustomerAsync"/>
/// (throwing <see cref="NotFoundException"/> if the booking doesn't exist) -> <see cref="Booking.Reschedule"/>
/// (every eligibility and date rule -- only an upcoming booking, no start in the past, end after start,
/// maximum length, price rescaled at the original daily rate) -> <see cref="BookingOverlapGuard"/> (the
/// new range must not clash with ANOTHER booking on the vehicle; this booking's own current range is
/// skipped) -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <see cref="BookingDto"/>.
/// The overlap check runs after the in-memory reschedule but before anything is saved, so a clash
/// leaves the database untouched. Two safety nets mirror the create handler and the other editors: a
/// Postgres exclusion violation (<c>23P01</c>, two bookings racing for the same dates) is turned back
/// into the identical overlap 409, and a <see cref="DbUpdateConcurrencyException"/> from the xmin token
/// (someone else edited/cancelled this booking first) becomes the clean 404/409 of
/// <see cref="ConcurrencyConflict"/> instead of a 500. No business rule lives here beyond that
/// orchestration (SRP).
/// </summary>
public class UpdateBookingCommandHandler(IBookingRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateBookingCommand, BookingDto>
{
    /// <summary>Postgres' SQLSTATE for a violated <c>EXCLUDE</c> constraint -- the overlap backstop.</summary>
    private const string ExclusionViolationSqlState = "23P01";

    public async Task<BookingDto> Handle(UpdateBookingCommand request, CancellationToken cancellationToken)
    {
        var (booking, vehicle, customer) =
            await repository.GetByIdWithVehicleAndCustomerAsync(request.BookingId, cancellationToken)
            ?? throw new NotFoundException(nameof(Booking), request.BookingId);

        booking.Reschedule(request.StartDate, request.EndDate);

        var newRange = new DateRange(booking.StartDate, booking.EndDate);
        await BookingOverlapGuard.EnsureNoOverlapAsync(
            repository, booking.VehicleId, newRange, excludingBookingId: booking.Id, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await ConcurrencyConflict.ResolveAsync(
                nameof(Booking), request.BookingId, () => repository.GetByIdAsync(request.BookingId, cancellationToken));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ExclusionViolationSqlState })
        {
            // Two requests raced for the same dates and both passed the check above; re-running it
            // re-derives the same 409 the sequential case gets (the conflicting row is still there).
            await BookingOverlapGuard.EnsureNoOverlapAsync(
                repository, booking.VehicleId, newRange, excludingBookingId: booking.Id, cancellationToken);
            throw;
        }

        return BookingDto.FromDomain(booking, vehicle, customer);
    }
}
