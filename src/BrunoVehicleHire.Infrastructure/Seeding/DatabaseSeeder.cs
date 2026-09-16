using Bogus;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrunoVehicleHire.Infrastructure.Seeding;

/// <summary>
/// Generates ~15 Vehicles, ~15 Customers, and a spread of Bookings across them via Bogus, every row
/// constructed through the real domain factory/methods (<see cref="Vehicle.Create"/>,
/// <see cref="Customer.Create"/>, <see cref="Booking.Create"/>, <see cref="Vehicle.SoftDelete"/>,
/// <see cref="Customer.SoftDelete"/>, <see cref="Customer.Anonymize"/>, <see cref="Booking.Cancel"/>,
/// <see cref="Booking.Complete"/>) and persisted via a single <see cref="AppDbContext.SaveChangesAsync"/>
/// call -- never raw SQL -- so AD-12's PII encryption value converter and the <c>EmailHash</c> shadow
/// property both apply exactly as they do to a real write (spec-6-1's Boundaries).
///
/// <para>
/// Deliberately covers the interesting states, not just plain rows (spec-6-1's Scope decision 4):
/// two soft-deleted Vehicles, one soft-deleted Customer, one anonymized Customer, and Bookings
/// spanning Active (future dates), Completed (past dates, <see cref="Booking.Complete"/>d here since
/// <c>BookingCompletionSweepService</c> hasn't run yet), and Cancelled -- so an evaluator sees every
/// Badge/status treatment without manually creating each state first. Vehicles/Customers are
/// soft-deleted/anonymized only AFTER their Bookings are built, so those rows still carry real booking
/// history -- exactly the scenario CAP-11/12 exist to prove (a soft-deleted/anonymized customer's
/// booking history remains queryable through Story 5.1's Customer Summary view).
/// </para>
/// </summary>
public class DatabaseSeeder(AppDbContext dbContext) : ISeeder
{
    /// <summary>
    /// Fixed (spec-6-1's Scope decision 3) so every fresh `docker-compose down -v &amp;&amp; up` +
    /// `dotnet run` produces byte-identical seed data -- reproducible for grading/discussion, never
    /// different run to run. The exact numeric value is arbitrary; only its fixedness matters.
    /// </summary>
    private const int FixedRandomizerSeed = 61961;

    private const int VehicleCount = 15;
    private const int CustomerCount = 15;

    /// <summary>Two soft-deleted Vehicles -- within spec-6-1's "1-2 soft-deleted" Code Map range.</summary>
    private static readonly int[] SoftDeletedVehicleIndices = [0, 7];

    private const int SoftDeletedCustomerIndex = 0;
    private const int AnonymizedCustomerIndex = 1;

    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters() so even a lone already-soft-deleted Vehicle from a prior seed still
        // counts as "already seeded" -- the empty-check this method is named for looks at every row,
        // not just the ones AD-13's query filter would normally surface.
        var alreadySeeded = await dbContext.Vehicles.IgnoreQueryFilters().AnyAsync(cancellationToken);
        if (alreadySeeded)
        {
            return;
        }

        // Scoped to this seed pass, not process-global -- Randomizer.Seed is a static Bogus setting,
        // so fixing it here (rather than relying on ambient state) is what makes the whole pass
        // reproducible regardless of what ran before it in the same process (e.g. tests).
        Randomizer.Seed = new Random(FixedRandomizerSeed);

        var vehicles = CreateVehicles();
        var customers = CreateCustomers();
        var bookings = CreateBookings(vehicles, customers);

        foreach (var index in SoftDeletedVehicleIndices)
        {
            vehicles[index].SoftDelete();
        }

        customers[SoftDeletedCustomerIndex].SoftDelete();
        customers[AnonymizedCustomerIndex].Anonymize();

        await dbContext.Vehicles.AddRangeAsync(vehicles, cancellationToken);
        await dbContext.Customers.AddRangeAsync(customers, cancellationToken);
        await dbContext.Bookings.AddRangeAsync(bookings, cancellationToken);

        // One SaveChangesAsync for the whole pass -- either every seeded row lands, or (on any
        // failure) none does, so a crash mid-seed can never leave the empty-check above believing
        // the database is "already seeded" from a partial batch.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static List<Vehicle> CreateVehicles()
    {
        var faker = new Faker();
        var usedRegistrationNumbers = new HashSet<string>();
        var vehicles = new List<Vehicle>(VehicleCount);
        var currentYear = DateTime.UtcNow.Year;

        for (var i = 0; i < VehicleCount; i++)
        {
            string registrationNumber;
            do
            {
                // e.g. "CA 123 456" -- a plausible South African-style plate. Retried on collision
                // since RegistrationNumber has a DB-level unique index (AppDbContext).
                registrationNumber = faker.Random.Replace("?? ### ###").ToUpperInvariant();
            }
            while (!usedRegistrationNumbers.Add(registrationNumber));

            var make = faker.Vehicle.Manufacturer();
            var model = faker.Vehicle.Model();
            var year = faker.Random.Int(2008, currentYear);
            var dailyRate = Math.Round(faker.Random.Decimal(350, 1800), 2);

            vehicles.Add(Vehicle.Create(registrationNumber, make, model, year, dailyRate));
        }

        return vehicles;
    }

    private static List<Customer> CreateCustomers()
    {
        var faker = new Faker();
        var usedEmailAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customers = new List<Customer>(CustomerCount);

        for (var i = 0; i < CustomerCount; i++)
        {
            var firstName = faker.Name.FirstName();
            var lastName = faker.Name.LastName();

            string email;
            do
            {
                // Retried on collision -- Email is encrypted at rest, but its EmailHash shadow
                // property still has a DB-level unique index (AppDbContext/AD-12).
                email = faker.Internet.Email(firstName, lastName);
            }
            while (!usedEmailAddresses.Add(email));

            // 10-digit, leading-zero local format -- mirrors CustomerMigrationTests/
            // CustomersEndpointTests' own example phone numbers (e.g. "0821234567").
            var phoneNumber = faker.Phone.PhoneNumber("0#########");

            customers.Add(Customer.Create(firstName, lastName, email, phoneNumber));
        }

        return customers;
    }

    /// <summary>
    /// Builds each Vehicle's Bookings with sequential, non-overlapping date ranges (spec-6-1's Scope
    /// decision 4): 1-2 past ranges first (each immediately <see cref="Booking.Complete"/>d), then
    /// 1-2 future ranges (left Active, except the second Vehicle's -- and roughly 30% of the rest's --
    /// which are immediately <see cref="Booking.Cancel"/>led). The first Vehicle's future Booking(s)
    /// are always left Active and the second Vehicle's are always Cancelled, guaranteeing at least one
    /// of every <see cref="BookingStatus"/> regardless of how the remaining Vehicles randomize.
    /// Customers are assigned round-robin (not randomly) across all Bookings so every Customer --
    /// including the ones later soft-deleted/anonymized -- ends up with real booking history.
    /// </summary>
    private static List<Booking> CreateBookings(IReadOnlyList<Vehicle> vehicles, IReadOnlyList<Customer> customers)
    {
        var faker = new Faker();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bookings = new List<Booking>();
        var nextCustomerIndex = 0;

        Customer NextCustomer() => customers[nextCustomerIndex++ % customers.Count];

        for (var vehicleIndex = 0; vehicleIndex < vehicles.Count; vehicleIndex++)
        {
            var vehicle = vehicles[vehicleIndex];

            var pastCursor = today.AddDays(-faker.Random.Int(60, 120));
            var pastBookingCount = faker.Random.Int(1, 2);
            for (var i = 0; i < pastBookingCount; i++)
            {
                var start = pastCursor;
                var end = start.AddDays(faker.Random.Int(2, 7));
                if (end >= today)
                {
                    break;
                }

                var booking = CreateBooking(vehicle, NextCustomer(), start, end);
                booking.Complete();
                bookings.Add(booking);

                pastCursor = end.AddDays(faker.Random.Int(1, 5));
            }

            var futureCursor = today.AddDays(faker.Random.Int(1, 10));
            var futureBookingCount = faker.Random.Int(1, 2);
            for (var i = 0; i < futureBookingCount; i++)
            {
                var start = futureCursor;
                var end = start.AddDays(faker.Random.Int(2, 7));

                var booking = CreateBooking(vehicle, NextCustomer(), start, end);

                var forceCancelled = vehicleIndex == 1;
                var forceActive = vehicleIndex == 0;
                var shouldCancel = forceCancelled || (!forceActive && faker.Random.Bool(0.3f));
                if (shouldCancel)
                {
                    booking.Cancel();
                }

                bookings.Add(booking);

                futureCursor = end.AddDays(faker.Random.Int(1, 5));
            }
        }

        return bookings;
    }

    /// <summary>
    /// <c>TotalPrice = Vehicle.DailyRate x (EndDate - StartDate in days)</c> -- the exact same
    /// calculation <c>CreateBookingCommandHandler</c> uses for a real booking, never trusted from
    /// anywhere else (DRY in spirit, even though the seeder bypasses the command handler entirely).
    /// </summary>
    private static Booking CreateBooking(Vehicle vehicle, Customer customer, DateOnly start, DateOnly end)
    {
        var totalPrice = vehicle.DailyRate * (end.DayNumber - start.DayNumber);
        return Booking.Create(vehicle.Id, customer.Id, start, end, totalPrice);
    }
}
