using BrunoVehicleHire.Application.Common;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// Orchestrates <see cref="CreateVehicleCommand"/>: a pre-insert duplicate-RegistrationNumber
/// existence check (AD-8's own named example) -> the domain factory (which itself still enforces
/// the Year plausible-range invariant, letting <see cref="DomainRuleViolationException"/> propagate
/// unmodified) -> <see cref="IVehicleRepository.AddAsync"/> -> exactly one
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, wrapped so the rare race of two near-simultaneous
/// requests both passing the pre-check (the real unique index on RegistrationNumber catches this at
/// commit time) is translated back into the identical 409 shape, mirroring
/// <see cref="Bookings.Commands.CreateBookingCommandHandler"/>'s own AD-7 layer-2 defense -> mapped
/// <see cref="VehicleDto"/>. Contains no validation logic of its own -- input-shape rules live in
/// <see cref="CreateVehicleCommandValidator"/> (via the pipeline), domain invariants live in
/// <see cref="Vehicle.Create"/>.
/// </summary>
public class CreateVehicleCommandHandler(IVehicleRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateVehicleCommand, VehicleDto>
{
    /// <summary>
    /// Postgres' SQLSTATE for a violated unique constraint -- the real unique index on
    /// RegistrationNumber -- distinct from <see cref="Bookings.Commands.CreateBookingCommandHandler"/>'s
    /// own <c>ExclusionViolationSqlState</c> ("23P01"), which guards a different constraint type.
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    public async Task<VehicleDto> Handle(CreateVehicleCommand request, CancellationToken cancellationToken)
    {
        await EnsureRegistrationNumberIsUniqueAsync(request.RegistrationNumber, cancellationToken);

        var vehicle = Vehicle.Create(
            request.RegistrationNumber, request.Make, request.Model, request.Year, request.DailyRate);

        await repository.AddAsync(vehicle, cancellationToken);

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
            await EnsureRegistrationNumberIsUniqueAsync(request.RegistrationNumber, cancellationToken);
            throw;
        }

        return VehicleDto.FromDomain(vehicle);
    }

    private async Task EnsureRegistrationNumberIsUniqueAsync(
        string registrationNumber, CancellationToken cancellationToken)
    {
        var alreadyExists = await repository.ExistsByRegistrationNumberAsync(
            Vehicle.NormalizeRegistrationNumber(registrationNumber), excludingId: null, cancellationToken);

        if (alreadyExists)
        {
            throw new DomainRuleViolationException(
                "Vehicle", "RegistrationNumber", "This registration number is already in use.");
        }
    }
}
