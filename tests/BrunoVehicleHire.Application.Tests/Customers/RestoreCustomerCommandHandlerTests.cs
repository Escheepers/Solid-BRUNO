using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Proves <see cref="RestoreCustomerCommandHandler"/>'s orchestration:
/// <c>GetByIdIncludingSoftDeletedAsync</c> (mocked <see cref="ICustomerRepository"/>) -> not-found
/// check -> <c>customer.Restore()</c>, letting <see cref="DomainRuleViolationException"/> propagate
/// unmodified -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>. Written before the handler
/// existed (TDD) -- mirrors <c>RestoreVehicleCommandHandlerTests</c>'s exact shape.
/// </summary>
public class RestoreCustomerCommandHandlerTests
{
    private static Customer SoftDeletedCustomer()
    {
        var customer = Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");
        customer.SoftDelete();
        return customer;
    }

    private static Customer ActiveCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    [Fact]
    public async Task Handle_SoftDeletedCustomer_RestoresAndSavesExactlyOnce()
    {
        var customer = SoftDeletedCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);

        var handler = new RestoreCustomerCommandHandler(repository, unitOfWork);
        var command = new RestoreCustomerCommand(customer.Id);

        await handler.Handle(command, CancellationToken.None);

        customer.IsDeleted.Should().BeFalse();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyActiveCustomer_ThrowsDomainRuleViolationException_AndNeverSaves()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);

        var handler = new RestoreCustomerCommandHandler(repository, unitOfWork);
        var command = new RestoreCustomerCommand(customer.Id);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Already active.");
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Rule.Should().Be(nameof(Customer.IsDeleted));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExistAtAll_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdIncludingSoftDeletedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var handler = new RestoreCustomerCommandHandler(repository, unitOfWork);
        var command = new RestoreCustomerCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
