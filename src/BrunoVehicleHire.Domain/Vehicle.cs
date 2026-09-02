namespace BrunoVehicleHire.Domain;

/// <summary>
/// Schema-only shape for the Vehicle aggregate, matching domain-model.md exactly.
/// Story 1.3 enriches this same file with <c>Vehicle.Create(...)</c>, private setters,
/// and invariant checks -- the column shape (names/types) does not change, so no second
/// migration is needed. Do not add behavior or a second "V2" entity here.
/// </summary>
public class Vehicle
{
    public Guid Id { get; set; }

    public string RegistrationNumber { get; set; } = string.Empty;

    public string Make { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int Year { get; set; }

    public decimal DailyRate { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedDate { get; set; }
}
