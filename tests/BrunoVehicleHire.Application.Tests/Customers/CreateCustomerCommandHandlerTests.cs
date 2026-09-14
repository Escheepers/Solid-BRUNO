using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Proves <see cref="CreateCustomerCommandHandler"/>'s orchestration: existence check (mocked
/// <see cref="ICustomerRepository"/>) -> domain factory -> <c>AddAsync</c> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/> -> mapped <c>CustomerDto</c>; and that a duplicate
/// Email short-circuits before any persistence call, throwing
/// <see cref="DomainRuleViolationException"/> with the exact AC message. Mirrors
/// <c>CreateVehicleCommandHandlerTests</c>'s exact coverage.
/// </summary>
public class CreateCustomerCommandHandlerTests
{
    private static CreateCustomerCommand ValidCommand() =>
        new("Jane", "Doe", "jane.doe@example.com", "0821234567");

    [Fact]
    public async Task Handle_NewEmail_ReturnsMappedDtoAndSavesOnce()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.ExistsByEmailAsync("jane.doe@example.com", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        Customer? addedCustomer = null;
        await repository.AddAsync(Arg.Do<Customer>(c => addedCustomer = c), Arg.Any<CancellationToken>());

        var handler = new CreateCustomerCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        addedCustomer.Should().NotBeNull();
        result.Id.Should().Be(addedCustomer!.Id);
        result.FirstName.Should().Be("Jane");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("jane.doe@example.com");
        result.PhoneNumber.Should().Be("0821234567");
        result.CreatedDate.Should().Be(addedCustomer.CreatedDate);

        await repository.Received(1).AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsDomainRuleViolationException_AndNeverPersists()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.ExistsByEmailAsync("jane.doe@example.com", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new CreateCustomerCommandHandler(repository, unitOfWork);

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("This email address is already in use.");
        exception.Which.Entity.Should().Be("Customer");
        exception.Which.Rule.Should().Be("Email");

        await repository.DidNotReceive().AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
