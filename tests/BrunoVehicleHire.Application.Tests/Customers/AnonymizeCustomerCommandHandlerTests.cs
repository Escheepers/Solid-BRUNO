using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
using FluentAssertions;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// Proves <see cref="AnonymizeCustomerCommandHandler"/>'s orchestration:
/// <c>GetByIdIncludingSoftDeletedAsync</c> (mocked <see cref="ICustomerRepository"/>) -> not-found
/// check -> <c>customer.Anonymize()</c> -> exactly one <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// Written before the handler existed (TDD) -- mirrors <c>RestoreCustomerCommandHandlerTests</c>'s
/// exact shape, since both handlers use the same unfiltered lookup.
/// </summary>
public class AnonymizeCustomerCommandHandlerTests
{
    private static Customer ActiveCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    private static Customer DeactivatedCustomer()
    {
        var customer = ActiveCustomer();
        customer.SoftDelete();
        return customer;
    }

    [Fact]
    public async Task Handle_ActiveCustomer_AnonymizesAndSavesExactlyOnce()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);

        var handler = new AnonymizeCustomerCommandHandler(repository, unitOfWork);
        var command = new AnonymizeCustomerCommand(customer.Id);

        await handler.Handle(command, CancellationToken.None);

        customer.IsAnonymized.Should().BeTrue();
        customer.FirstName.Should().Be("Anonymized");
        customer.LastName.Should().Be("Customer");
        customer.Email.Should().Be($"erased-{customer.Id}@anonymized.local");
        customer.PhoneNumber.Should().Be("0000000000");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeactivatedCustomer_AnonymizesAndLeavesIsDeletedTrue_AndSavesExactlyOnce()
    {
        var customer = DeactivatedCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);

        var handler = new AnonymizeCustomerCommandHandler(repository, unitOfWork);
        var command = new AnonymizeCustomerCommand(customer.Id);

        await handler.Handle(command, CancellationToken.None);

        customer.IsAnonymized.Should().BeTrue();
        customer.IsDeleted.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomerDoesNotExistAtAll_ThrowsNotFoundException_AndNeverSaves()
    {
        var repository = Substitute.For<ICustomerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var missingId = Guid.NewGuid();

        repository.GetByIdIncludingSoftDeletedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var handler = new AnonymizeCustomerCommandHandler(repository, unitOfWork);
        var command = new AnonymizeCustomerCommand(missingId);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Id.Should().Be(missingId);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
