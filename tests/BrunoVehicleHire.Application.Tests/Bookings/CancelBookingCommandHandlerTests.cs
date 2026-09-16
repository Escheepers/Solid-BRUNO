using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Proves <see cref="CancelBookingCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="IBookingRepository"/>) -> not-found check -> <c>booking.Cancel()</c> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, plus the three domain-exception pass-through cases
/// (already-Cancelled, already-Completed, past-StartDate-still-Active -- updated by
/// spec-booking-form-error-handling-fixes.md from the old past-EndDate-still-Active rule). Written
/// before the handler existed (TDD) -- mirrors <c>SoftDeleteVehicleCommandHandlerTests</c>'s exact
/// shape.
/// </summary>
public class CancelBookingCommandHandlerTests
{
    private static readonly DateOnly FutureStartDate = new(2026, 10, 1);
    private static readonly DateOnly FutureEndDate = new(2026, 10, 5);

    private static Booking ActiveFutureBooking() =>
        Booking.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), FutureStartDate, FutureEndDate, 1400m);

    [Fact]
    public async Task Handle_ExistingEligibleBooking_CallsCancelAndSavesExactlyOnce()
    {
        var booking = ActiveFutureBooking();
        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CancelBookingCommandHandler(repository, unitOfWork);
        var command = new CancelBookingCommand(booking.Id);

        await handler.Handle(command, CancellationToken.None);

        booking.Status.Should().Be(BookingStatus.Cancelled);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BookingDoesNotExist_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Booking?)null);

        var handler = new CancelBookingCommandHandler(repository, unitOfWork);
        var command = new CancelBookingCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Booking));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyCancelledBooking_PropagatesDomainRuleViolationException_AndNeverSaves()
    {
        var booking = ActiveFutureBooking();
        booking.Cancel(new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero)));

        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CancelBookingCommandHandler(repository, unitOfWork);
        var command = new CancelBookingCommand(booking.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<Domain.Exceptions.DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Cannot cancel — booking already cancelled.");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyCompletedBooking_PropagatesDomainRuleViolationException_AndNeverSaves()
    {
        var booking = ActiveFutureBooking();
        typeof(Booking).GetProperty(nameof(Booking.Status))!.SetValue(booking, BookingStatus.Completed);

        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CancelBookingCommandHandler(repository, unitOfWork);
        var command = new CancelBookingCommand(booking.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<Domain.Exceptions.DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Cannot cancel — booking has already started.");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StillActiveBookingWithPastEndDate_PropagatesDomainRuleViolationException_AndNeverSaves()
    {
        var booking = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 1400m);

        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CancelBookingCommandHandler(repository, unitOfWork);
        var command = new CancelBookingCommand(booking.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<Domain.Exceptions.DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Cannot cancel — booking has already started.");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Minimal fixed-clock test double -- avoids adding a mocking package, mirrors <c>BookingTests</c>'s own.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
