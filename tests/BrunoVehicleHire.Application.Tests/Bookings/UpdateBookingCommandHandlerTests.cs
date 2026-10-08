using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Proves <see cref="UpdateBookingCommandHandler"/>'s orchestration: load the booking with its vehicle
/// and customer (404 if missing) -> <c>Booking.Reschedule</c> -> overlap check against OTHER bookings
/// only -> exactly one save -> mapped DTO; a clash or an ineligible booking never saves; a concurrency
/// failure becomes the clean 404/409. Dates are far in the future because the handler uses the real clock.
/// </summary>
public class UpdateBookingCommandHandlerTests
{
    private static readonly DateOnly Start = new(2040, 6, 10);
    private static readonly DateOnly End = new(2040, 6, 14); // 4 days

    private static (Booking Booking, Vehicle Vehicle, Customer Customer) Existing()
    {
        var vehicle = Vehicle.Create("CA123456", "Toyota", "Corolla", 2023, 350m);
        var customer = Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");
        var booking = Booking.Create(vehicle.Id, customer.Id, Start, End, 1400m);
        return (booking, vehicle, customer);
    }

    private static (IBookingRepository Repository, IUnitOfWork UnitOfWork) Substitutes(
        (Booking Booking, Vehicle Vehicle, Customer Customer) existing)
    {
        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdWithVehicleAndCustomerAsync(existing.Booking.Id, Arg.Any<CancellationToken>())
            .Returns(existing);
        // The vehicle's own bookings include the one being moved.
        repository.GetNonCancelledForVehicleAsync(existing.Vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Booking> { existing.Booking });

        return (repository, unitOfWork);
    }

    [Fact]
    public async Task Handle_MovingIntoItsOwnOldRange_Succeeds_SavesOnce_AndRescalesThePrice()
    {
        var existing = Existing();
        var (repository, unitOfWork) = Substitutes(existing);
        var handler = new UpdateBookingCommandHandler(repository, unitOfWork);

        // Overlaps the booking's CURRENT range (10-14 Jun) but that must not count as a clash.
        var result = await handler.Handle(
            new UpdateBookingCommand(existing.Booking.Id, Start.AddDays(1), End.AddDays(2)),
            CancellationToken.None);

        result.StartDate.Should().Be(Start.AddDays(1));
        result.EndDate.Should().Be(End.AddDays(2));
        result.TotalPrice.Should().Be(1750m); // 5 days x 350
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewRangeOverlapsAnotherBooking_Throws409WithThatBookingsDates_AndNeverSaves()
    {
        var existing = Existing();
        var (repository, unitOfWork) = Substitutes(existing);
        var other = Booking.Create(existing.Vehicle.Id, Guid.NewGuid(), new DateOnly(2040, 7, 1), new DateOnly(2040, 7, 5), 1400m);
        repository.GetNonCancelledForVehicleAsync(existing.Vehicle.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Booking> { existing.Booking, other });
        var handler = new UpdateBookingCommandHandler(repository, unitOfWork);

        var act = async () => await handler.Handle(
            new UpdateBookingCommand(existing.Booking.Id, new DateOnly(2040, 7, 3), new DateOnly(2040, 7, 8)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("This vehicle is already booked 1 Jul – 5 Jul");
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BookingNotFound_ThrowsNotFound()
    {
        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var handler = new UpdateBookingCommandHandler(repository, unitOfWork);

        var act = async () => await handler.Handle(
            new UpdateBookingCommand(Guid.NewGuid(), Start, End), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CancelledBooking_ThrowsCannotEdit_AndNeverSaves()
    {
        var existing = Existing();
        existing.Booking.Cancel();
        var (repository, unitOfWork) = Substitutes(existing);
        var handler = new UpdateBookingCommandHandler(repository, unitOfWork);

        var act = async () => await handler.Handle(
            new UpdateBookingCommand(existing.Booking.Id, Start.AddDays(1), End.AddDays(1)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Cannot edit — booking is cancelled.");
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConcurrentChange_ThrowsTheCleanConflict_WhenTheBookingIsStillThere()
    {
        var existing = Existing();
        var (repository, unitOfWork) = Substitutes(existing);
        repository.GetByIdAsync(existing.Booking.Id, Arg.Any<CancellationToken>()).Returns(existing.Booking);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new DbUpdateConcurrencyException());
        var handler = new UpdateBookingCommandHandler(repository, unitOfWork);

        var act = async () => await handler.Handle(
            new UpdateBookingCommand(existing.Booking.Id, Start.AddDays(1), End.AddDays(1)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Rule.Should().Be(ConcurrencyConflict.Rule);
    }
}
