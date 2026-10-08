using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Proves <see cref="SoftDeleteCustomerCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="ICustomerRepository"/>) -> not-found check -> <c>customer.SoftDelete()</c> -> exactly
/// one <see cref="IUnitOfWork.SaveChangesAsync"/>. Written before the handler existed (TDD) -- mirrors
/// <c>SoftDeleteVehicleCommandHandlerTests</c>'s exact shape.
/// </summary>
public class SoftDeleteCustomerCommandHandlerTests
{
    private static Customer ExistingCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    [Fact]
    public async Task Handle_ExistingCustomer_CallsSoftDeleteAndSavesExactlyOnce()
    {
        var customer = ExistingCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var handler = new SoftDeleteCustomerCommandHandler(repository, Substitute.For<IBookingRepository>(), unitOfWork);
        var command = new SoftDeleteCustomerCommand(customer.Id);

        await handler.Handle(command, CancellationToken.None);

        customer.IsDeleted.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExist_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var handler = new SoftDeleteCustomerCommandHandler(repository, Substitute.For<IBookingRepository>(), unitOfWork);
        var command = new SoftDeleteCustomerCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerHasActiveBooking_ThrowsDomainRuleViolationException_AndNeverSoftDeletesOrSaves()
    {
        var customer = ExistingCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        bookingRepository.ExistsActiveForCustomerAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new SoftDeleteCustomerCommandHandler(repository, bookingRepository, unitOfWork);

        var act = async () => await handler.Handle(new SoftDeleteCustomerCommand(customer.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be(
            "This customer has an active or upcoming booking — cancel it first, or wait for it to complete.");
        exception.Which.Entity.Should().Be("Customer");
        exception.Which.Rule.Should().Be("HasActiveBookings");

        customer.IsDeleted.Should().BeFalse();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerHasOnlyCompletedOrCancelledBookings_StillDeactivatesAndSavesExactlyOnce()
    {
        // ExistsActiveForCustomerAsync only ever returns true for an Active booking, so a customer whose
        // bookings are all Completed/Cancelled (history) is fed false here and deactivates normally.
        var customer = ExistingCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        bookingRepository.ExistsActiveForCustomerAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new SoftDeleteCustomerCommandHandler(repository, bookingRepository, unitOfWork);

        await handler.Handle(new SoftDeleteCustomerCommand(customer.Id), CancellationToken.None);

        customer.IsDeleted.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExist_NeverChecksBookings()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var missingId = Guid.NewGuid();
        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var handler = new SoftDeleteCustomerCommandHandler(repository, bookingRepository, Substitute.For<IUnitOfWork>());

        var act = async () => await handler.Handle(new SoftDeleteCustomerCommand(missingId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await bookingRepository.DidNotReceive().ExistsActiveForCustomerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
