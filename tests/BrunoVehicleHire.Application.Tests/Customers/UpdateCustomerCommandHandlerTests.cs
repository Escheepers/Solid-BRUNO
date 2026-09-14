using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Proves <see cref="UpdateCustomerCommandHandler"/>'s orchestration: <c>GetByIdAsync</c> (mocked
/// <see cref="ICustomerRepository"/>) -> not-found check -> duplicate-excluding-self check ->
/// <c>customer.Update(...)</c> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped
/// <c>CustomerDto</c>. Critically covers the exclude-self regression: submitting a customer's own
/// unchanged Email must NOT be treated as a duplicate of itself. Mirrors
/// <c>UpdateVehicleCommandHandlerTests</c>'s exact coverage.
/// </summary>
public class UpdateCustomerCommandHandlerTests
{
    private static Customer ExistingCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    private static UpdateCustomerCommand CommandFor(
        Customer customer,
        string? email = null,
        string firstName = "Jane",
        string lastName = "Doe",
        string phoneNumber = "0839999999") =>
        new(customer.Id, firstName, lastName, email ?? customer.Email, phoneNumber);

    [Fact]
    public async Task Handle_ExistingCustomerWithNewEmail_UpdatesCustomerAndSavesOnce()
    {
        var customer = ExistingCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        repository.ExistsByEmailAsync("new.email@example.com", customer.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new UpdateCustomerCommandHandler(repository, unitOfWork);
        var command = CommandFor(
            customer, email: "new.email@example.com", firstName: "John", lastName: "Smith", phoneNumber: "0831112222");

        var result = await handler.Handle(command, CancellationToken.None);

        result.Id.Should().Be(customer.Id);
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Smith");
        result.Email.Should().Be("new.email@example.com");
        result.PhoneNumber.Should().Be("0831112222");

        customer.Email.Should().Be("new.email@example.com");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SubmittingCustomersOwnUnchangedEmail_DoesNotThrow()
    {
        // Regression: excludingId must be passed correctly (the customer's own id), not omitted or
        // inverted -- otherwise a customer's own current Email would be flagged as a duplicate of
        // itself.
        var customer = ExistingCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        repository.ExistsByEmailAsync(customer.Email, customer.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new UpdateCustomerCommandHandler(repository, unitOfWork);
        var command = CommandFor(customer, phoneNumber: "0835556666");

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().NotThrowAsync();
        customer.PhoneNumber.Should().Be("0835556666");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateEmailAgainstDifferentCustomer_ThrowsDomainRuleViolationException_AndNeverSaves()
    {
        var customer = ExistingCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        repository.ExistsByEmailAsync("taken@example.com", customer.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new UpdateCustomerCommandHandler(repository, unitOfWork);
        var command = CommandFor(customer, email: "taken@example.com");

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("This email address is already in use.");
        exception.Which.Entity.Should().Be("Customer");
        exception.Which.Rule.Should().Be("Email");

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExist_ThrowsNotFoundException_AndNeverChecksDuplicateOrSaves()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var handler = new UpdateCustomerCommandHandler(repository, unitOfWork);
        var command = new UpdateCustomerCommand(missingId, "Jane", "Doe", "jane.doe@example.com", "0821234567");

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);

        await repository.DidNotReceive().ExistsByEmailAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
