using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BrunoVehicleHire.Infrastructure.Persistence;

/// <summary>
/// EF Core composition root for the Postgres database. DbSets for Vehicle (unique index on
/// RegistrationNumber) and Customer (PII columns transparently encrypted -- see below), each with a
/// global query filter (AD-13) so soft-deleted rows never surface through any EF Core query anywhere
/// in the app, present or future. Additional aggregates arrive with their own stories.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// The Data Protection purpose string scoping every key derived for PII encryption -- a fixed,
    /// stable value (changing it would make every previously-encrypted row unreadable).
    /// </summary>
    private const string PiiProtectorPurpose = "BrunoVehicleHire.PII";

    private readonly IDataProtector _piiProtector;

    public AppDbContext(DbContextOptions<AppDbContext> options, IDataProtectionProvider dataProtectionProvider)
        : base(options)
    {
        _piiProtector = dataProtectionProvider.CreateProtector(PiiProtectorPurpose);
    }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("Vehicles");

            entity.HasKey(v => v.Id);

            entity.Property(v => v.RegistrationNumber)
                .IsRequired();

            entity.HasIndex(v => v.RegistrationNumber)
                .IsUnique();

            entity.Property(v => v.Make)
                .IsRequired();

            entity.Property(v => v.Model)
                .IsRequired();

            entity.Property(v => v.DailyRate)
                .HasColumnType("numeric(18,2)");

            // AD-13: soft-deleted vehicles never surface through EF Core queries anywhere in the
            // app -- query-time only, so this needs no new migration.
            entity.HasQueryFilter(v => !v.IsDeleted);

            // Optimistic-concurrency guard: Postgres' own xmin system column, already present on
            // every row, mapped as an EF Core concurrency token -- no schema migration needed.
            // Npgsql.EntityFrameworkCore.PostgreSQL 10.x removed the old `UseXminAsConcurrencyToken()`
            // convenience method (present only up to 8.x) in favor of its own model-finalizing
            // convention (NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty), which
            // detects any uint shadow property configured as a concurrency token with
            // ValueGeneratedOnAddOrUpdate -- exactly what IsRowVersion() configures below -- and maps
            // it directly onto the real system column instead of creating a new one. Lets
            // RestoreVehicleCommandHandler detect and correctly react to a concurrent double-click
            // that would otherwise silently double-process (see Design Notes).
            entity.Property<uint>("xmin")
                .IsRowVersion();
        });

        // AD-12's first real implementation: Email/PhoneNumber are encrypted transparently via a
        // ValueConverter backed by the Data Protection API -- Customer (Domain) never knows this
        // happens. Data Protection is non-deterministic (a random IV per call), so the actual unique
        // constraint for "Email unique" lives on the EmailHash shadow property (a deterministic
        // SHA-256 of the normalized email, computed in SaveChangesAsync below) instead of on the
        // encrypted Email column itself, which could never enforce it correctly.
        var piiConverter = new ValueConverter<string, string>(
            plaintext => _piiProtector.Protect(plaintext),
            ciphertext => _piiProtector.Unprotect(ciphertext));

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.FirstName)
                .IsRequired();

            entity.Property(c => c.LastName)
                .IsRequired();

            entity.Property(c => c.Email)
                .IsRequired()
                .HasConversion(piiConverter);

            entity.Property(c => c.PhoneNumber)
                .IsRequired()
                .HasConversion(piiConverter);

            // Persistence-only uniqueness sidecar -- see EmailHasher's and this class's doc comments.
            // Customer (Domain) has no property for this; it exists purely as an EF Core shadow
            // property, never exposed outside Infrastructure.
            entity.Property<string>("EmailHash")
                .IsRequired();

            entity.HasIndex("EmailHash")
                .IsUnique();

            // AD-13: mirrors Vehicle's exact query filter pattern. Story 3.5 extends it to also
            // exclude IsAnonymized customers from default listings and booking selection -- the same
            // query-filter mechanism as IsDeleted, not a new one (spec-3-5's Scope decision 3).
            // GetPagedAsync's includeInactive flag already bypasses this filter wholesale via
            // IgnoreQueryFilters(), so toggling "show inactive" surfaces anonymized customers too.
            entity.HasQueryFilter(c => !c.IsDeleted && !c.IsAnonymized);
        });

        // Booking (Story 3.3): the first enum and the first FK relationships anywhere in this
        // schema. Booking has no navigation properties back to Vehicle/Customer (the minimal schema
        // this story needs), so each FK is configured via HasOne<T>().WithMany().HasForeignKey(...)
        // with no corresponding navigation -- EF Core's supported shape for a unidirectional,
        // navigation-free relationship. No query filter here -- Booking has no IsDeleted concept.
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");

            entity.HasKey(b => b.Id);

            // DateOnly maps to Postgres' native "date" column automatically (EF Core 10 +
            // Npgsql.EntityFrameworkCore.PostgreSQL 10) -- no explicit HasColumnType needed, unlike
            // DailyRate/TotalPrice's decimal precision below.
            entity.Property(b => b.TotalPrice)
                .HasColumnType("numeric(18,2)");

            // The first enum anywhere in this schema: stored as its string name, not its numeric
            // ordinal, so the column stays human-readable and insertion-order-independent if the
            // enum's members are ever reordered.
            entity.Property(b => b.Status)
                .HasConversion<string>();

            // Postgres does not auto-index FK columns (unlike some other databases) -- routine
            // schema hygiene, not scope creep. Restrict (not the EF Core default of Cascade)
            // matches domain-model.md's cross-entity constraint that a Booking must never be
            // silently orphaned or cascade-deleted out from under an existing Vehicle/Customer row.
            entity.HasOne<Vehicle>()
                .WithMany()
                .HasForeignKey(b => b.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(b => b.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(b => b.VehicleId);

            entity.HasIndex(b => b.CustomerId);

            // Optimistic-concurrency guard: mirrors Vehicle's own xmin mapping exactly -- lets
            // CancelBookingCommandHandler detect and correctly react to a concurrent double-click
            // that would otherwise silently double-process (see Design Notes).
            entity.Property<uint>("xmin")
                .IsRowVersion();
        });
    }

    /// <summary>
    /// Computes and sets the EmailHash shadow property for every added/modified Customer BEFORE
    /// calling the base implementation -- this is the one place that still has both the plaintext
    /// Email (readable here, before the value converter transforms it for the parameter) and direct
    /// access to the shadow property. See spec-3-1's Design Notes for why this can't live in
    /// Customer.Create() instead.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetEmailHashesForPendingCustomers();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        SetEmailHashesForPendingCustomers();
        return base.SaveChanges();
    }

    private void SetEmailHashesForPendingCustomers()
    {
        foreach (var entry in ChangeTracker.Entries<Customer>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property("EmailHash").CurrentValue = EmailHasher.Compute(entry.Entity.Email);
            }
        }
    }
}
