using BrunoVehicleHire.Domain;
using FluentValidation;

namespace BrunoVehicleHire.Application.Vehicles.Commands;

/// <summary>
/// The one definition of what a vehicle daily rate may be, shared by
/// <see cref="CreateVehicleCommandValidator"/> and <see cref="UpdateVehicleCommandValidator"/> so the
/// two can never drift apart: positive, at most <see cref="Vehicle.MaxDailyRate"/>, and a whole number
/// of cents (the price columns are <c>numeric(18,2)</c>, so more decimals would be silently rounded and
/// a huge value would overflow into a 500). 400-level shape checks; <c>Vehicle</c> enforces the same
/// invariants as defense-in-depth.
/// </summary>
public static class DailyRateRules
{
    public static IRuleBuilderOptions<T, decimal> MustBeAValidDailyRate<T>(this IRuleBuilder<T, decimal> rule)
    {
        return rule
            .GreaterThan(0)
            .LessThanOrEqualTo(Vehicle.MaxDailyRate)
            .WithMessage($"'{{PropertyName}}' must not exceed {Vehicle.MaxDailyRate:N0}.")
            .Must(Vehicle.IsWholeCents)
            .WithMessage("'{PropertyName}' must have at most 2 decimal places.");
    }
}
