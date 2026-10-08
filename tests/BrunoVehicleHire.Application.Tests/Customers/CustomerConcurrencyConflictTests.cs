using BrunoVehicleHire.Application.Bookings;
using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BrunoVehicleHire.Application.Tests.Customers;

/// <summary>
/// What every Customer mutation does when the Customer's xmin concurrency token rejects its save
/// (<see cref="DbUpdateConcurrencyException"/>: another request changed the row between this request's
/// read and write). Customer previously had no such token, so a double-clicked Restore/Deactivate/Erase
/// silently "succeeded" twice; with it, the loser must get a clean 4xx, never a 500. Restore mirrors
/// <c>RestoreVehicleCommandHandler</c> (re-invoke the domain method so IT reports the true state);
/// the others re-read: if the customer is no longer visible it is a 404, otherwise a 409
/// "changed by someone else".
/// </summary>
public class CustomerConcurrencyConflictTests
{
    private static Customer ActiveCustomer() =>
        Customer.Create("Jane", "Doe", "jane.doe@example.com", "0821234567");

    private static IUnitOfWork FailingUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException());
        return unitOfWork;
    }

    private static async Task AssertConcurrencyConflict(Func<Task> act)
    {
        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Entity.Should().Be(nameof(Customer));
        exception.Which.Rule.Should().Be("ConcurrencyConflict");
        exception.Which.Message.Should().Be("This customer was changed by someone else just now. Reload and try again.");
    }

    // ---- Restore: re-invoke Restore() so the domain reports the true state ("Already active.") ----

    [Fact]
    public async Task Restore_LostTheRace_ReInvokesRestore_SoTheDomainReportsAlreadyActive()
    {
        var customer = ActiveCustomer();
        customer.SoftDelete();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var handler = new RestoreCustomerCommandHandler(repository, FailingUnitOfWork());

        var act = async () => await handler.Handle(new RestoreCustomerCommand(customer.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DomainRuleViolationException>();
        exception.Which.Message.Should().Be("Already active.");
        exception.Which.Rule.Should().Be(nameof(Customer.IsDeleted));
    }

    // ---- SoftDelete ----

    [Fact]
    public async Task SoftDelete_LostTheRace_CustomerNowDeactivated_ThrowsNotFound()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer, (Customer?)null);

        var handler = new SoftDeleteCustomerCommandHandler(repository, Substitute.For<IBookingRepository>(), FailingUnitOfWork());

        var act = async () => await handler.Handle(new SoftDeleteCustomerCommand(customer.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SoftDelete_ConflictWhileCustomerStillVisible_ThrowsConcurrencyConflict409()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var handler = new SoftDeleteCustomerCommandHandler(repository, Substitute.For<IBookingRepository>(), FailingUnitOfWork());

        await AssertConcurrencyConflict(
            () => handler.Handle(new SoftDeleteCustomerCommand(customer.Id), CancellationToken.None));
    }

    // ---- Anonymize: never report success unless the scrub really was saved ----

    [Fact]
    public async Task Anonymize_ConflictingSave_ThrowsConcurrencyConflict409_NeverSilentlySucceeds()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var handler = new AnonymizeCustomerCommandHandler(repository, Substitute.For<IBookingRepository>(), FailingUnitOfWork());

        await AssertConcurrencyConflict(
            () => handler.Handle(new AnonymizeCustomerCommand(customer.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Anonymize_ConflictingSave_CustomerGoneMeanwhile_ThrowsNotFound()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdIncludingSoftDeletedAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer, (Customer?)null);

        var handler = new AnonymizeCustomerCommandHandler(repository, Substitute.For<IBookingRepository>(), FailingUnitOfWork());

        var act = async () => await handler.Handle(new AnonymizeCustomerCommand(customer.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- HardDelete ----

    [Fact]
    public async Task HardDelete_LostTheRace_CustomerAlreadyDeleted_ThrowsNotFound()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer, (Customer?)null);

        var handler = new HardDeleteCustomerCommandHandler(repository, bookingRepository, FailingUnitOfWork());

        var act = async () => await handler.Handle(new HardDeleteCustomerCommand(customer.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HardDelete_ConflictWhileCustomerStillVisible_ThrowsConcurrencyConflict409()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        var bookingRepository = Substitute.For<IBookingRepository>();
        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var handler = new HardDeleteCustomerCommandHandler(repository, bookingRepository, FailingUnitOfWork());

        await AssertConcurrencyConflict(
            () => handler.Handle(new HardDeleteCustomerCommand(customer.Id), CancellationToken.None));
    }

    // ---- Update ----

    [Fact]
    public async Task Update_LostTheRace_CustomerNoLongerVisible_ThrowsNotFound()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer, (Customer?)null);

        var handler = new UpdateCustomerCommandHandler(repository, FailingUnitOfWork());

        var act = async () => await handler.Handle(
            new UpdateCustomerCommand(customer.Id, "Janet", "Doe", "jane.doe@example.com", "0821234567"),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_ConflictWhileCustomerStillVisible_ThrowsConcurrencyConflict409()
    {
        var customer = ActiveCustomer();
        var repository = Substitute.For<ICustomerRepository>();
        repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var handler = new UpdateCustomerCommandHandler(repository, FailingUnitOfWork());

        await AssertConcurrencyConflict(() => handler.Handle(
            new UpdateCustomerCommand(customer.Id, "Janet", "Doe", "jane.doe@example.com", "0821234567"),
            CancellationToken.None));
    }
}
