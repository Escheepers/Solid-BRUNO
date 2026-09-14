using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Bookings;

/// <summary>
/// Proves <see cref="CompleteBookingCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="IBookingRepository"/>) -> not-found check -> <c>booking.Complete()</c> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, plus domain-exception pass-through (already-Cancelled,
/// still-eligible-future-EndDate). Written before the handler existed (TDD) -- mirrors
/// <c>CancelBookingCommandHandlerTests</c>'s exact shape.
/// </summary>
public class CompleteBookingCommandHandlerTests
{
    private static readonly DateOnly PastStartDate = new(2026, 9, 1);
    private static readonly DateOnly PastEndDate = new(2026, 9, 5);

    private static Booking ActivePastBooking() =>
        Booking.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), PastStartDate, PastEndDate, 1400m);

    [Fact]
    public async Task Handle_ExistingEligibleBooking_CallsCompleteAndSavesExactlyOnce()
    {
        var booking = ActivePastBooking();
        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CompleteBookingCommandHandler(repository, unitOfWork);
        var command = new CompleteBookingCommand(booking.Id);

        await handler.Handle(command, CancellationToken.None);

        booking.Status.Should().Be(BookingStatus.Completed);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BookingDoesNotExist_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Booking?)null);

        var handler = new CompleteBookingCommandHandler(repository, unitOfWork);
        var command = new CompleteBookingCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Booking));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyCancelledBooking_PropagatesDomainRuleViolationException_AndNeverSaves()
    {
        var booking = ActivePastBooking();
        typeof(Booking).GetProperty(nameof(Booking.Status))!.SetValue(booking, BookingStatus.Cancelled);

        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CompleteBookingCommandHandler(repository, unitOfWork);
        var command = new CompleteBookingCommand(booking.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<Domain.Exceptions.DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Cannot complete — booking already cancelled.");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StillActiveBookingWithFutureEndDate_PropagatesDomainRuleViolationException_AndNeverSaves()
    {
        var booking = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CompleteBookingCommandHandler(repository, unitOfWork);
        var command = new CompleteBookingCommand(booking.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<Domain.Exceptions.DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Cannot complete — booking has not ended yet.");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyCompletedBooking_IsIdempotent_AndStillSaves()
    {
        // Complete() is a no-op on an already-Completed booking, but the handler still calls
        // SaveChangesAsync once -- it has no idempotency short-circuit of its own (SRP: that
        // decision belongs entirely to Booking.Complete, not this handler).
        var booking = ActivePastBooking();
        booking.Complete();

        var repository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var handler = new CompleteBookingCommandHandler(repository, unitOfWork);
        var command = new CompleteBookingCommand(booking.Id);

        await handler.Handle(command, CancellationToken.None);

        booking.Status.Should().Be(BookingStatus.Completed);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
