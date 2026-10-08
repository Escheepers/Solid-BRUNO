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
/// Proves <see cref="HardDeleteCustomerCommandHandler"/>'s orchestration (spec-3-3): fetch the
/// customer (mocked <see cref="ICustomerRepository"/>, 404 if missing) -&gt; has-bookings check
/// (mocked <see cref="IBookingRepository"/>, 409 if true, with the exact AC message) -&gt;
/// <c>RemoveAsync</c> -&gt; exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Written before the
/// handler existed (TDD) -- mirrors <c>SoftDeleteVehicleCommandHandlerTests</c>'s exact shape.
/// </summary>
public class HardDeleteCustomerCommandHandlerTests
{
    private static Customer ExistingCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    [Fact]
    public async Task Handle_CustomerWithNoBookings_RemovesAndSavesExactlyOnce()
    {
        var customer = ExistingCustomer();
        var customerRepository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        customerRepository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        bookingRepository.ExistsForCustomerAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new HardDeleteCustomerCommandHandler(customerRepository, bookingRepository, unitOfWork);
        var command = new HardDeleteCustomerCommand(customer.Id);

        await handler.Handle(command, CancellationToken.None);

        await customerRepository.Received(1).RemoveAsync(customer, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerWithBookings_ThrowsDomainRuleViolationException_AndNeverRemovesOrSaves()
    {
        var customer = ExistingCustomer();
        var customerRepository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        customerRepository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        bookingRepository.ExistsForCustomerAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new HardDeleteCustomerCommandHandler(customerRepository, bookingRepository, unitOfWork);
        var command = new HardDeleteCustomerCommand(customer.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be(
            "This customer has bookings — deactivate or erase their data instead.");
        exception.Which.Entity.Should().Be("Customer");
        exception.Which.Rule.Should().Be("HasBookings");

        await customerRepository.DidNotReceive().RemoveAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerWithActiveBooking_ThrowsTheCancelItFirstMessage_NotTheDeactivateOrEraseOne()
    {
        var customer = ExistingCustomer();
        var customerRepository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        customerRepository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        bookingRepository.ExistsForCustomerAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(true);
        bookingRepository.ExistsActiveForCustomerAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new HardDeleteCustomerCommandHandler(customerRepository, bookingRepository, unitOfWork);

        var act = async () => await handler.Handle(new HardDeleteCustomerCommand(customer.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be(
            "This customer has an active or upcoming booking — cancel it first, or wait for it to complete.");
        exception.Which.Rule.Should().Be("HasActiveBookings");

        await customerRepository.DidNotReceive().RemoveAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExist_ThrowsNotFoundException_AndNeverChecksBookingsOrSaves()
    {
        var customerRepository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        customerRepository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var handler = new HardDeleteCustomerCommandHandler(customerRepository, bookingRepository, unitOfWork);
        var command = new HardDeleteCustomerCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);

        await bookingRepository.DidNotReceive().ExistsForCustomerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await customerRepository.DidNotReceive().RemoveAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
