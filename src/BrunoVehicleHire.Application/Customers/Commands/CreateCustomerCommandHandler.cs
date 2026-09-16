using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Customers.Dtos;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// Orchestrates <see cref="CreateCustomerCommand"/>: a pre-insert duplicate-Email existence check
/// (mirrors <c>CreateVehicleCommandHandler</c>'s duplicate-RegistrationNumber check) -> the domain
/// factory -> <see cref="ICustomerRepository.AddAsync"/> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, wrapped so the rare race of two near-simultaneous
/// requests both passing the pre-check (the real unique index on the EmailHash shadow property
/// catches this at commit time) is translated back into the identical 409 shape, mirroring
/// <c>CreateVehicleCommandHandler</c>'s own defense -> mapped <see cref="CustomerDto"/>. Contains no
/// validation logic of its own -- input-shape rules live in
/// <see cref="CreateCustomerCommandValidator"/> (via the pipeline), domain invariants live in
/// <see cref="Customer.Create"/>. This handler only orchestrates: it never computes an EmailHash,
/// never touches <c>IDataProtector</c> -- both are entirely <c>AppDbContext</c>'s concern
/// (Infrastructure), reached only through <see cref="ICustomerRepository"/>'s abstraction.
/// </summary>
public class CreateCustomerCommandHandler(ICustomerRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    /// <summary>
    /// Postgres' SQLSTATE for a violated unique constraint -- the real unique index on the EmailHash
    /// shadow property -- mirrors <c>CreateVehicleCommandHandler</c>'s own named constant exactly.
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        await EnsureEmailIsUniqueAsync(request.Email, cancellationToken);

        var customer = Customer.Create(
            request.FirstName, request.LastName, request.Email, request.PhoneNumber);

        await repository.AddAsync(customer, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            // The rare race this catch exists for: two concurrent requests both passed the
            // pre-check above before either committed. Re-running the identical existence check
            // makes this 409 byte-identical in shape to the fast-path above -- the just-committed
            // row is, by definition, still there to be found again.
            await EnsureEmailIsUniqueAsync(request.Email, cancellationToken);
            throw;
        }

        return CustomerDto.FromDomain(customer);
    }

    private async Task EnsureEmailIsUniqueAsync(string email, CancellationToken cancellationToken)
    {
        var alreadyExists = await repository.ExistsByEmailAsync(email, excludingId: null, cancellationToken);

        if (alreadyExists)
        {
            throw new DomainRuleViolationException(
                "Customer", "Email", "This email address is already in use.");
        }
    }
}
