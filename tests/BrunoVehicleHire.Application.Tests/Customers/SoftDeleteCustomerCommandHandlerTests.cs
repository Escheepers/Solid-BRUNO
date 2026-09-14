using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
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

        var handler = new SoftDeleteCustomerCommandHandler(repository, unitOfWork);
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

        var handler = new SoftDeleteCustomerCommandHandler(repository, unitOfWork);
        var command = new SoftDeleteCustomerCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
